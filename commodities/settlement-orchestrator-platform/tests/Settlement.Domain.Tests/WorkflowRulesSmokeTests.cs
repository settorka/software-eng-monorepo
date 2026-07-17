using Settlement.Domain.Common;
using Settlement.Domain.Trades;
using Settlement.Domain.Workflows;

namespace Settlement.Domain.Tests;

public static class WorkflowRulesSmokeTests
{
    public static void NewWorkflowStartsPendingAndCanValidate()
    {
        var trade = Trade.Create(
            "TRD-1",
            1,
            "POWER",
            "CP-1",
            100,
            "MWH",
            10,
            "GBP",
            new DateOnly(2026, 7, 17),
            new DateOnly(2026, 7, 31),
            "hash");

        var workflow = SettlementWorkflow.Start(trade, "corr", DateTimeOffset.UtcNow);
        workflow.TransitionTo(WorkflowState.Validating, "accepted", "corr", "corr", DateTimeOffset.UtcNow);

        if (workflow.State != WorkflowState.Validating)
        {
            throw new InvalidOperationException("Workflow did not transition to Validating.");
        }

        if (workflow.WorkflowVersion != 2)
        {
            throw new InvalidOperationException("Workflow version did not increment.");
        }
    }

    public static void IllegalTransitionIsRejected()
    {
        try
        {
            WorkflowTransitionPolicy.EnsureAllowed(WorkflowState.Pending, WorkflowState.Completed);
        }
        catch (DomainException)
        {
            return;
        }

        throw new InvalidOperationException("Illegal transition was accepted.");
    }
}
