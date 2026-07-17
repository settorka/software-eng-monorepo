using System.Collections.Concurrent;
using Settlement.Application.Trades;
using Settlement.Domain.Trades;
using Settlement.Domain.Workflows;

namespace Settlement.Infrastructure.Trades;

public sealed class InMemoryTradeWorkflowStore : ITradeWorkflowStore
{
    private readonly ConcurrentDictionary<(string TradeId, int TradeVersion), StoredTradeWorkflow> _workflows = [];
    private readonly ConcurrentDictionary<Guid, (string TradeId, int TradeVersion)> _workflowIndex = [];

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

        _workflowIndex[workflow.WorkflowId] = (trade.TradeId, trade.TradeVersion);

        return Task.CompletedTask;
    }

    public Task<StoredTradeWorkflow?> FindByWorkflowIdAsync(
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_workflowIndex.TryGetValue(workflowId, out var key))
        {
            return Task.FromResult<StoredTradeWorkflow?>(null);
        }

        _workflows.TryGetValue(key, out var workflow);
        return Task.FromResult(workflow);
    }

    public Task<IReadOnlyCollection<StoredTradeWorkflow>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyCollection<StoredTradeWorkflow> workflows = _workflows.Values
            .OrderBy(workflow => workflow.Workflow.CreatedAt)
            .ToArray();

        return Task.FromResult(workflows);
    }

    public Task UpdateWorkflowAsync(
        SettlementWorkflow workflow,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_workflowIndex.TryGetValue(workflow.WorkflowId, out var key))
        {
            throw new InvalidOperationException($"Workflow {workflow.WorkflowId} was not found.");
        }

        if (!_workflows.TryGetValue(key, out var stored))
        {
            throw new InvalidOperationException($"Workflow {workflow.WorkflowId} was not found.");
        }

        _workflows[key] = stored with { Workflow = workflow };
        return Task.CompletedTask;
    }
}
