namespace Settlement.Infrastructure.Persistence.Entities;

public sealed class InvoiceEntity
{
    public Guid InvoiceId { get; set; }

    public Guid SettlementId { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; set; }

    public SettlementEntity? Settlement { get; set; }

    public PaymentRequestEntity? PaymentRequest { get; set; }
}
