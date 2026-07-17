namespace Settlement.Domain.Workflows;

public sealed record WorkflowTransition(
    WorkflowState From,
    WorkflowState To,
    string Reason);

