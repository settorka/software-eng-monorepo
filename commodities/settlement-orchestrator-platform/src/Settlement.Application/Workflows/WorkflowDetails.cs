using Settlement.Domain.Audit;
using Settlement.Domain.Workflows;

namespace Settlement.Application.Workflows;

public sealed record WorkflowDetails(
    Guid WorkflowId,
    string TradeId,
    int TradeVersion,
    WorkflowState State,
    int WorkflowVersion,
    Guid? SettlementId,
    decimal? SettlementAmount,
    Guid? InvoiceId,
    Guid? PaymentRequestId,
    IReadOnlyCollection<WorkflowTransition> Transitions,
    IReadOnlyCollection<AuditEvent> AuditEvents);
