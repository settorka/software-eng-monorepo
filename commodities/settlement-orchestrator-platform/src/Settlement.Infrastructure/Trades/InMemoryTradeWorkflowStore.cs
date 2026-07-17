using System.Collections.Concurrent;
using Settlement.Application.Trades;
using Settlement.Domain.Trades;
using Settlement.Domain.Workflows;

namespace Settlement.Infrastructure.Trades;

public sealed class InMemoryTradeWorkflowStore : ITradeWorkflowStore
{
    private readonly ConcurrentDictionary<(string TradeId, int TradeVersion), StoredTradeWorkflow> _workflows = [];

    public Task<StoredTradeWorkflow?> FindByTradeVersionAsync(
        string tradeId,
        int tradeVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _workflows.TryGetValue((tradeId, tradeVersion), out var workflow);
        return Task.FromResult(workflow);
    }

    public Task AddAsync(
        Trade trade,
        SettlementWorkflow workflow,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stored = new StoredTradeWorkflow(trade, workflow, idempotencyKey);

        if (!_workflows.TryAdd((trade.TradeId, trade.TradeVersion), stored))
        {
            throw new InvalidOperationException($"Trade {trade.TradeId} version {trade.TradeVersion} already exists.");
        }

        return Task.CompletedTask;
    }
}

