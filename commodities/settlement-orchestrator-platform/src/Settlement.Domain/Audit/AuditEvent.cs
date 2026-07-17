namespace Settlement.Domain.Audit;

public sealed record AuditEvent(
    Guid AuditEventId,
    Guid WorkflowId,
    string TradeId,
    int TradeVersion,
    string EventType,
    string CorrelationId,
    string CausationId,
    DateTimeOffset OccurredAt,
    string Details);

