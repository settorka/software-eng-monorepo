namespace Settlement.Infrastructure.Persistence.Entities;

public sealed class TradeEntity
{
    public string TradeId { get; set; } = string.Empty;

    public int TradeVersion { get; set; }

    public string Commodity { get; set; } = string.Empty;

    public string Counterparty { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTime TradeDate { get; set; }

    public DateTime SettlementDate { get; set; }

    public string PayloadHash { get; set; } = string.Empty;

    public WorkflowEntity? Workflow { get; set; }
}
