namespace Settlement.Infrastructure.Persistence.Entities;

public sealed class AuditEventEntity
{
    public Guid AuditEventId { get; set; }

    public Guid WorkflowId { get; set; }

    public string TradeId { get; set; } = string.Empty;

    public int TradeVersion { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string CausationId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string Details { get; set; } = string.Empty;

    public WorkflowEntity? Workflow { get; set; }
}
