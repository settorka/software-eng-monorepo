using Settlement.Application.Trades;

namespace Settlement.Application.Workflows;

public sealed class GetWorkflowHandler(ITradeWorkflowStore store)
{
    public async Task<WorkflowDetails?> HandleAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var stored = await store.FindByWorkflowIdAsync(workflowId, cancellationToken);

        if (stored is null)
        {
            return null;
        }

        var workflow = stored.Workflow;

        return new WorkflowDetails(
            workflow.WorkflowId,
            workflow.TradeId,
            workflow.TradeVersion,
            workflow.State,
            workflow.WorkflowVersion,
            workflow.Transitions,
            workflow.AuditEvents);
    }
}

