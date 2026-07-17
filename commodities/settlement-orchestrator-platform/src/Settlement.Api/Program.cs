using Settlement.Api.Contracts;
using Settlement.Application.Trades;
using Settlement.Application.Workflows;
using Settlement.Domain.Common;
using Settlement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSettlementInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapGet("/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost(
    "/api/v1/trades",
    async (
        ReceiveTradeRequest request,
        HttpContext httpContext,
        ReceiveTradeHandler handler,
        CancellationToken cancellationToken) =>
    {
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
            return Results.BadRequest(new { error = exception.Message });
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

app.Run();

static string GetCorrelationId(HttpContext httpContext)
{
    return httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var headerCorrelationId) &&
        !string.IsNullOrWhiteSpace(headerCorrelationId)
            ? headerCorrelationId.ToString()
            : Guid.NewGuid().ToString("N");
}
