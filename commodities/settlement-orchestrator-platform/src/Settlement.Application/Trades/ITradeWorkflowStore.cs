using Settlement.Domain.Trades;
using Settlement.Domain.Workflows;

namespace Settlement.Application.Trades;

public interface ITradeWorkflowStore
{
    Task<StoredTradeWorkflow?> FindByTradeVersionAsync(
        string tradeId,
        int tradeVersion,
        CancellationToken cancellationToken);

    Task AddAsync(
        Trade trade,
        SettlementWorkflow workflow,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed record StoredTradeWorkflow(
    Trade Trade,
    SettlementWorkflow Workflow,
    string IdempotencyKey);

