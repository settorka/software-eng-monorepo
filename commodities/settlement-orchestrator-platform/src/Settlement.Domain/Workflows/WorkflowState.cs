namespace Settlement.Domain.Workflows;

public enum WorkflowState
{
    Pending,
    Validating,
    Calculating,
    AwaitingApproval,
    Approved,
    InvoiceGenerating,
    InvoiceGenerated,
    PaymentPublishing,
    PaymentRequested,
    Completed,
    Failed,
    Retrying,
    DeadLetter,
    Rejected,
    Superseded
}
