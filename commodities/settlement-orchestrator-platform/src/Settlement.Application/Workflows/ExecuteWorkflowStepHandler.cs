using Settlement.Application.Common;
using Settlement.Application.Trades;
using Settlement.Domain.Common;
using Settlement.Domain.Workflows;

namespace Settlement.Application.Workflows;

public sealed class ExecuteWorkflowStepHandler(
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
        var now = clock.UtcNow;

        switch (workflow.State)
        {
            case WorkflowState.Validating:
                workflow.TransitionTo(WorkflowState.Calculating, "Trade passed validation.", correlationId, correlationId, now);
                workflow.TransitionTo(WorkflowState.AwaitingApproval, "Settlement calculated using placeholder rules.", correlationId, correlationId, now);
                break;

            case WorkflowState.Approved:
                workflow.TransitionTo(WorkflowState.InvoiceGenerating, "Approval received.", correlationId, correlationId, now);
                workflow.TransitionTo(WorkflowState.InvoiceGenerated, "Invoice generated using placeholder adapter.", correlationId, correlationId, now);
                workflow.TransitionTo(WorkflowState.PaymentPublishing, "Invoice ready for payment request.", correlationId, correlationId, now);
                workflow.TransitionTo(WorkflowState.PaymentRequested, "Payment request recorded in placeholder publisher.", correlationId, correlationId, now);
                break;

            case WorkflowState.PaymentRequested:
                workflow.TransitionTo(WorkflowState.Completed, "Payment request lifecycle completed in placeholder flow.", correlationId, correlationId, now);
                break;

            default:
                throw new DomainException($"Workflow {workflow.WorkflowId} has no executable step in state {workflow.State}.");
        }

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

