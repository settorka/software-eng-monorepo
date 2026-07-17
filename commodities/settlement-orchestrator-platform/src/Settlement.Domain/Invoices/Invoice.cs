namespace Settlement.Domain.Invoices;

public sealed record Invoice(
    Guid InvoiceId,
    Guid SettlementId,
    string InvoiceNumber,
    DateTimeOffset GeneratedAt);

