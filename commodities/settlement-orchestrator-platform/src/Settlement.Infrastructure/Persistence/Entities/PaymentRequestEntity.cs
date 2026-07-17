namespace Settlement.Infrastructure.Persistence.Entities;

public sealed class PaymentRequestEntity
{
    public Guid PaymentRequestId { get; set; }

    public Guid InvoiceId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    public InvoiceEntity? Invoice { get; set; }
}
