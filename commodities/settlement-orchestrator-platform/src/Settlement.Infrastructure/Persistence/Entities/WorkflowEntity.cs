namespace Settlement.Infrastructure.Persistence.Entities;

public sealed class WorkflowEntity
{
    public Guid WorkflowId { get; set; }

    public string TradeId { get; set; } = string.Empty;

    public int TradeVersion { get; set; }

    public string State { get; set; } = string.Empty;

    public int WorkflowVersion { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public TradeEntity? Trade { get; set; }

    public List<WorkflowTransitionEntity> Transitions { get; set; } = [];

    public List<AuditEventEntity> AuditEvents { get; set; } = [];

    public SettlementEntity? Settlement { get; set; }
}
