using Settlement.Application.Common;
using Settlement.Domain.Trades;
using Settlement.Domain.Workflows;

namespace Settlement.Application.Trades;

public sealed class ReceiveTradeHandler(
    ITradeWorkflowStore store,
    IClock clock)
{
    public async Task<ReceiveTradeResult> HandleAsync(
        ReceiveTradeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);

        var payloadHash = StablePayloadHash.From(command with
        {
            IdempotencyKey = string.Empty,
            CorrelationId = string.Empty
        });

        var existing = await store.FindByTradeVersionAsync(
            command.TradeId,
            command.TradeVersion,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.Trade.PayloadHash != payloadHash)
            {
                throw new DuplicateTradeConflictException(command.TradeId, command.TradeVersion);
            }

            return new ReceiveTradeResult(
                existing.Workflow.WorkflowId,
                existing.Workflow.TradeId,
                existing.Workflow.TradeVersion,
                existing.Workflow.State,
                WasDuplicate: true);
        }

        var trade = Trade.Create(
            command.TradeId,
            command.TradeVersion,
            command.Commodity,
            command.Counterparty,
            command.Quantity,
            command.Unit,
            command.Price,
            command.Currency,
            command.TradeDate,
            command.SettlementDate,
            payloadHash);

        var workflow = SettlementWorkflow.Start(trade, command.CorrelationId, clock.UtcNow);
        workflow.TransitionTo(
            WorkflowState.Validating,
            "Trade accepted for validation.",
            command.CorrelationId,
            command.CorrelationId,
            clock.UtcNow);

        await store.AddAsync(trade, workflow, command.IdempotencyKey, cancellationToken);

        return new ReceiveTradeResult(
            workflow.WorkflowId,
            workflow.TradeId,
            workflow.TradeVersion,
            workflow.State,
            WasDuplicate: false);
    }
}

