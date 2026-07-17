using Settlement.Application.Trades;

namespace Settlement.Application.Workflows;

public sealed class ListWorkflowsHandler(ITradeWorkflowStore store)
{
    public async Task<IReadOnlyCollection<WorkflowDetails>> HandleAsync(CancellationToken cancellationToken)
    {
        var stored = await store.ListAsync(cancellationToken);

        return stored
            .Select(item => new WorkflowDetails(
                item.Workflow.WorkflowId,
                item.Workflow.TradeId,
                item.Workflow.TradeVersion,
                item.Workflow.State,
                item.Workflow.WorkflowVersion,
                item.Workflow.Transitions,
                item.Workflow.AuditEvents))
            .ToArray();
    }
}

