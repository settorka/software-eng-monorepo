using Settlement.Domain.Common;

namespace Settlement.Domain.Workflows;

public static class WorkflowTransitionPolicy
{
    private static readonly IReadOnlySet<(WorkflowState From, WorkflowState To)> AllowedTransitions =
        new HashSet<(WorkflowState From, WorkflowState To)>
        {
            (WorkflowState.Pending, WorkflowState.Validating),
            (WorkflowState.Validating, WorkflowState.Calculating),
            (WorkflowState.Validating, WorkflowState.Failed),
            (WorkflowState.Calculating, WorkflowState.AwaitingApproval),
            (WorkflowState.Calculating, WorkflowState.Retrying),
            (WorkflowState.AwaitingApproval, WorkflowState.Approved),
            (WorkflowState.AwaitingApproval, WorkflowState.Rejected),
            (WorkflowState.AwaitingApproval, WorkflowState.Superseded),
            (WorkflowState.Approved, WorkflowState.InvoiceGenerating),
            (WorkflowState.InvoiceGenerating, WorkflowState.InvoiceGenerated),
            (WorkflowState.InvoiceGenerating, WorkflowState.Retrying),
            (WorkflowState.InvoiceGenerated, WorkflowState.PaymentPublishing),
            (WorkflowState.PaymentPublishing, WorkflowState.PaymentRequested),
            (WorkflowState.PaymentPublishing, WorkflowState.Retrying),
            (WorkflowState.PaymentRequested, WorkflowState.Completed),
            (WorkflowState.Retrying, WorkflowState.Calculating),
            (WorkflowState.Retrying, WorkflowState.InvoiceGenerating),
            (WorkflowState.Retrying, WorkflowState.PaymentPublishing),
            (WorkflowState.Retrying, WorkflowState.DeadLetter),
            (WorkflowState.Failed, WorkflowState.Retrying)
        };

    private static readonly IReadOnlySet<WorkflowState> TerminalStates =
        new HashSet<WorkflowState>
        {
            WorkflowState.Completed,
            WorkflowState.Rejected,
            WorkflowState.Superseded,
            WorkflowState.DeadLetter
        };

    public static bool IsTerminal(WorkflowState state)
    {
        return TerminalStates.Contains(state);
    }

    public static void EnsureAllowed(WorkflowState from, WorkflowState to)
    {
        if (AllowedTransitions.Contains((from, to)))
        {
            return;
        }

        throw new DomainException($"Workflow transition {from} -> {to} is not allowed.");
    }
}

