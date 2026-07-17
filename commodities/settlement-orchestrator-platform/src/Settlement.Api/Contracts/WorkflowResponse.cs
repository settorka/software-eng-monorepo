using Settlement.Application.Workflows;
using Settlement.Domain.Workflows;

namespace Settlement.Api.Contracts;

public sealed record WorkflowResponse(
    Guid WorkflowId,
    string TradeId,
    int TradeVersion,
    WorkflowState State,
    int WorkflowVersion,
    int TransitionCount,
    int AuditEventCount)
{
    public static WorkflowResponse From(WorkflowDetails workflow)
    {
        return new WorkflowResponse(
            workflow.WorkflowId,
            workflow.TradeId,
            workflow.TradeVersion,
            workflow.State,
            workflow.WorkflowVersion,
            workflow.Transitions.Count,
            workflow.AuditEvents.Count);
    }
}

