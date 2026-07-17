namespace Settlement.Domain.Payments;

public sealed record PaymentRequest(
    Guid PaymentRequestId,
    Guid InvoiceId,
    string IdempotencyKey,
    DateTimeOffset RequestedAt);

