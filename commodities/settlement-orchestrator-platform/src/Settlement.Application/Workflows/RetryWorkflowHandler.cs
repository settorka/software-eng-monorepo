using Settlement.Application.Common;
using Settlement.Application.Trades;
using Settlement.Domain.Common;
using Settlement.Domain.Workflows;

namespace Settlement.Application.Workflows;

public sealed class RetryWorkflowHandler(
    ITradeWorkflowStore store,
    IClock clock)
{
    public async Task<WorkflowDetails> HandleAsync(
        Guid workflowId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var stored = await store.FindByWorkflowIdAsync(workflowId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow {workflowId} was not found.");

        var workflow = stored.Workflow;

        if (workflow.State != WorkflowState.Failed)
        {
            throw new DomainException($"Workflow {workflowId} is {workflow.State}; only Failed workflows can be retried in this slice.");
        }

        workflow.TransitionTo(WorkflowState.Retrying, "Operator retry requested.", correlationId, correlationId, clock.UtcNow);
        await store.UpdateWorkflowAsync(workflow, cancellationToken);

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

