using Settlement.Api.Contracts;
using Settlement.Api.Configuration;
using Settlement.Api.Observability;
using Settlement.Application.Trades;
using Settlement.Application.Workflows;
using Settlement.Domain.Common;
using Settlement.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddOpenApi();
builder.Services.Configure<OperationalControlsOptions>(
    builder.Configuration.GetSection(OperationalControlsOptions.SectionName));
builder.Services.AddHealthChecks();
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("settlement-api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
builder.Services.AddSettlementInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));
app.MapHealthChecks("/health", new HealthCheckOptions());
app.MapMetrics();

app.MapPost(
    "/api/v1/trades",
    async (
        ReceiveTradeRequest request,
        HttpContext httpContext,
        ReceiveTradeHandler handler,
        Microsoft.Extensions.Options.IOptions<OperationalControlsOptions> controls,
        CancellationToken cancellationToken) =>
    {
        if (!controls.Value.IntakeEnabled)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (httpContext.Request.ContentLength > controls.Value.MaxRequestBodyBytes)
        {
            return Results.BadRequest(new { error = "Request body exceeds configured limit." });
        }

        if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey) ||
            string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = "Idempotency-Key header is required." });
        }

        var correlationId = httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var headerCorrelationId) &&
            !string.IsNullOrWhiteSpace(headerCorrelationId)
                ? headerCorrelationId.ToString()
                : Guid.NewGuid().ToString("N");

        var command = new ReceiveTradeCommand(
            request.TradeId,
            request.TradeVersion,
            request.Commodity,
            request.Counterparty,
            request.Quantity,
            request.Unit,
            request.Price,
            request.Currency,
            request.TradeDate,
            request.SettlementDate,
            idempotencyKey.ToString(),
            correlationId);

        try
        {
            var result = await handler.HandleAsync(command, cancellationToken);

            return Results.Accepted(
                value: new ReceiveTradeResponse(
                    result.WorkflowId,
                    result.TradeId,
                    result.TradeVersion,
                    result.State,
                    result.WasDuplicate));
        }
        catch (DuplicateTradeConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = controls.Value.DetailedErrors ? exception.Message : "Invalid trade request." });
        }
    });

app.MapGet(
    "/api/v1/workflows",
    async (ListWorkflowsHandler handler, CancellationToken cancellationToken) =>
    {
        var workflows = await handler.HandleAsync(cancellationToken);
        return Results.Ok(workflows.Select(WorkflowResponse.From));
    });

app.MapGet(
    "/api/v1/workflows/{workflowId:guid}",
    async (Guid workflowId, GetWorkflowHandler handler, CancellationToken cancellationToken) =>
    {
        var workflow = await handler.HandleAsync(workflowId, cancellationToken);

        return workflow is null
            ? Results.NotFound(new { error = $"Workflow {workflowId} was not found." })
            : Results.Ok(WorkflowResponse.From(workflow));
    });

app.MapPost(
    "/api/v1/workflows/{workflowId:guid}/execute",
    async (
        Guid workflowId,
        HttpContext httpContext,
        ExecuteWorkflowStepHandler handler,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var workflow = await handler.HandleAsync(workflowId, GetCorrelationId(httpContext), cancellationToken);
            return Results.Ok(WorkflowResponse.From(workflow));
        }
        catch (DomainException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    });

app.MapPost(
    "/api/v1/settlements/{workflowId:guid}/approve",
    async (
        Guid workflowId,
        HttpContext httpContext,
        ApproveWorkflowHandler handler,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var workflow = await handler.HandleAsync(workflowId, GetCorrelationId(httpContext), cancellationToken);
            return Results.Ok(WorkflowResponse.From(workflow));
        }
        catch (DomainException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    });

app.MapPost(
    "/api/v1/workflows/{workflowId:guid}/retry",
    async (
        Guid workflowId,
        HttpContext httpContext,
        RetryWorkflowHandler handler,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var workflow = await handler.HandleAsync(workflowId, GetCorrelationId(httpContext), cancellationToken);
            return Results.Ok(WorkflowResponse.From(workflow));
        }
        catch (DomainException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    });

app.MapPost(
    "/api/v1/workflows/pump",
    async (
        HttpContext httpContext,
        PumpWorkflowsHandler handler,
        Microsoft.Extensions.Options.IOptions<OperationalControlsOptions> controls,
        CancellationToken cancellationToken) =>
    {
        if (!controls.Value.WorkflowPumpEnabled)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var workflows = await handler.HandleAsync(
            GetCorrelationId(httpContext),
            controls.Value.MaxPumpWorkflows,
            cancellationToken);
        return Results.Ok(workflows.Select(WorkflowResponse.From));
    });

app.Run();

static string GetCorrelationId(HttpContext httpContext)
{
    return httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var headerCorrelationId) &&
        !string.IsNullOrWhiteSpace(headerCorrelationId)
            ? headerCorrelationId.ToString()
            : Guid.NewGuid().ToString("N");
}

public partial class Program;
