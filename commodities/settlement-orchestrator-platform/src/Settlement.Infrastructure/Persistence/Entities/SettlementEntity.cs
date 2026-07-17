namespace Settlement.Infrastructure.Persistence.Entities;

public sealed class SettlementEntity
{
    public Guid SettlementId { get; set; }

    public Guid WorkflowId { get; set; }

    public string TradeId { get; set; } = string.Empty;

    public int TradeVersion { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTimeOffset CalculatedAt { get; set; }

    public WorkflowEntity? Workflow { get; set; }

    public InvoiceEntity? Invoice { get; set; }
}
