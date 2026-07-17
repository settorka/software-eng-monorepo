using Settlement.Domain.Common;
using Settlement.Domain.Trades;
using Settlement.Domain.Workflows;
using Xunit;

namespace Settlement.Domain.Tests;

public sealed class WorkflowTransitionPolicyTests
{
    [Fact]
    public void WorkflowVersionIncrementsOnValidTransition()
    {
        var workflow = SettlementWorkflow.Start(CreateTrade(), "corr", DateTimeOffset.UtcNow);

        workflow.TransitionTo(WorkflowState.Validating, "accepted", "corr", "corr", DateTimeOffset.UtcNow);

        Assert.Equal(WorkflowState.Validating, workflow.State);
        Assert.Equal(2, workflow.WorkflowVersion);
        Assert.Single(workflow.Transitions);
        Assert.Equal(2, workflow.AuditEvents.Count);
    }

    [Fact]
    public void IllegalTransitionIsRejected()
    {
        Assert.Throws<DomainException>(() =>
            WorkflowTransitionPolicy.EnsureAllowed(WorkflowState.Pending, WorkflowState.Completed));
    }

    private static Trade CreateTrade()
    {
        return Trade.Create(
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
    }
}
