using Settlement.Api.Contracts;
using Settlement.Api.Configuration;
using Settlement.Api.Observability;
using Settlement.Application.Trades;
using Settlement.Application.Workflows;
using Settlement.Domain.Common;
using Settlement.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Settlement.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddOpenApi();
builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = false;
});
builder.Services.Configure<OperationalControlsOptions>(
    builder.Configuration.GetSection(OperationalControlsOptions.SectionName));
builder.Services.AddSingleton<SettlementBusinessMetrics>();
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

if (authOptions.Enabled)
{
    if (string.IsNullOrWhiteSpace(authOptions.Authority) || string.IsNullOrWhiteSpace(authOptions.Audience))
    {
        throw new InvalidOperationException("Auth is enabled; Auth:Authority and Auth:Audience are required.");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authOptions.Authority;
            options.Audience = authOptions.Audience;
            options.RequireHttpsMetadata = authOptions.RequireHttpsMetadata;
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("TradeService", policy => policy.RequireRole("settlement.service"));
        options.AddPolicy("Operator", policy => policy.RequireRole("settlement.operator"));
        options.AddPolicy("Approver", policy => policy.RequireRole("settlement.approver"));
    });
}
else
{
    builder.Services.AddAuthorization();
}

var healthChecks = builder.Services.AddHealthChecks();
var oracleConnectionString = builder.Configuration.GetConnectionString("Oracle");
if (!string.IsNullOrWhiteSpace(oracleConnectionString))
{
    healthChecks.AddOracle(oracleConnectionString, name: "oracle");
}
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
await app.Services.GetRequiredService<SettlementDatabaseMigrator>().MigrateAsync(CancellationToken.None);

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseHttpMetrics();

if (authOptions.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/live", () => Results.Ok(new { status = "live" }));
app.MapHealthChecks("/ready", new HealthCheckOptions());
app.MapHealthChecks("/health", new HealthCheckOptions());
app.MapMetrics();

var receiveTradeEndpoint = app.MapPost(
    "/api/v1/trades",
    async (
        ReceiveTradeRequest request,
        HttpContext httpContext,
        ReceiveTradeHandler handler,
        SettlementBusinessMetrics businessMetrics,
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
            businessMetrics.RecordTradeAccepted(request.Commodity, request.Currency, result.WasDuplicate);
            businessMetrics.RecordWorkflowState(result.State);

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
RequirePolicyIfAuthEnabled(receiveTradeEndpoint, authOptions, "TradeService");

var listWorkflowsEndpoint = app.MapGet(
    "/api/v1/workflows",
    async (
        ListWorkflowsHandler handler,
        SettlementBusinessMetrics businessMetrics,
        CancellationToken cancellationToken) =>
    {
        var workflows = await handler.HandleAsync(cancellationToken);
        businessMetrics.SetWorkflowStateCounts(workflows);
        return Results.Ok(workflows.Select(WorkflowResponse.From));
    });
RequirePolicyIfAuthEnabled(listWorkflowsEndpoint, authOptions, "Operator");

var getWorkflowEndpoint = app.MapGet(
    "/api/v1/workflows/{workflowId:guid}",
    async (Guid workflowId, GetWorkflowHandler handler, CancellationToken cancellationToken) =>
    {
        var workflow = await handler.HandleAsync(workflowId, cancellationToken);

        return workflow is null
            ? Results.NotFound(new { error = $"Workflow {workflowId} was not found." })
            : Results.Ok(WorkflowResponse.From(workflow));
    });
RequirePolicyIfAuthEnabled(getWorkflowEndpoint, authOptions, "Operator");

var executeWorkflowEndpoint = app.MapPost(
    "/api/v1/workflows/{workflowId:guid}/execute",
    async (
        Guid workflowId,
        HttpContext httpContext,
        ExecuteWorkflowStepHandler handler,
        SettlementBusinessMetrics businessMetrics,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var workflow = await handler.HandleAsync(workflowId, GetCorrelationId(httpContext), cancellationToken);
            businessMetrics.RecordWorkflowState(workflow.State);
            businessMetrics.RecordCompletionLatency(workflow, DateTimeOffset.UtcNow);
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
RequirePolicyIfAuthEnabled(executeWorkflowEndpoint, authOptions, "Operator");

var approveWorkflowEndpoint = app.MapPost(
    "/api/v1/settlements/{workflowId:guid}/approve",
    async (
        Guid workflowId,
        HttpContext httpContext,
        ApproveWorkflowHandler handler,
        SettlementBusinessMetrics businessMetrics,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var workflow = await handler.HandleAsync(workflowId, GetCorrelationId(httpContext), cancellationToken);
            businessMetrics.RecordWorkflowState(workflow.State);
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
RequirePolicyIfAuthEnabled(approveWorkflowEndpoint, authOptions, "Approver");

var retryWorkflowEndpoint = app.MapPost(
    "/api/v1/workflows/{workflowId:guid}/retry",
    async (
        Guid workflowId,
        HttpContext httpContext,
        RetryWorkflowHandler handler,
        SettlementBusinessMetrics businessMetrics,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var workflow = await handler.HandleAsync(workflowId, GetCorrelationId(httpContext), cancellationToken);
            businessMetrics.RecordWorkflowState(workflow.State);
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
RequirePolicyIfAuthEnabled(retryWorkflowEndpoint, authOptions, "Operator");

var pumpWorkflowsEndpoint = app.MapPost(
    "/api/v1/workflows/pump",
    async (
        HttpContext httpContext,
        PumpWorkflowsHandler handler,
        SettlementBusinessMetrics businessMetrics,
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
        foreach (var workflow in workflows)
        {
            businessMetrics.RecordWorkflowState(workflow.State);
            businessMetrics.RecordCompletionLatency(workflow, DateTimeOffset.UtcNow);
        }

        return Results.Ok(workflows.Select(WorkflowResponse.From));
    });
RequirePolicyIfAuthEnabled(pumpWorkflowsEndpoint, authOptions, "Operator");

app.Run();

static string GetCorrelationId(HttpContext httpContext)
{
    return httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var headerCorrelationId) &&
        !string.IsNullOrWhiteSpace(headerCorrelationId)
            ? headerCorrelationId.ToString()
            : Guid.NewGuid().ToString("N");
}

static void RequirePolicyIfAuthEnabled(
    RouteHandlerBuilder endpoint,
    AuthOptions authOptions,
    string policyName)
{
    if (authOptions.Enabled)
    {
        endpoint.RequireAuthorization(new AuthorizeAttribute { Policy = policyName });
    }
}

public partial class Program;
