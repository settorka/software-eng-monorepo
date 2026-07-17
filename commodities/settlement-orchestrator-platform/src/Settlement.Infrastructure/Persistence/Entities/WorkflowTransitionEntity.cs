namespace Settlement.Infrastructure.Persistence.Entities;

public sealed class WorkflowTransitionEntity
{
    public long WorkflowTransitionId { get; set; }

    public Guid WorkflowId { get; set; }

    public int Sequence { get; set; }

    public string FromState { get; set; } = string.Empty;

    public string ToState { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public WorkflowEntity? Workflow { get; set; }
}
