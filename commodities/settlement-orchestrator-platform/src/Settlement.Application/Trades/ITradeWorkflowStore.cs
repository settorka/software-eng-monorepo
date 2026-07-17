using Settlement.Domain.Trades;
using Settlement.Domain.Workflows;
using Settlement.Domain.Settlements;
using Settlement.Domain.Invoices;
using Settlement.Domain.Payments;
using Settlement.Domain.Outbox;

namespace Settlement.Application.Trades;

public interface ITradeWorkflowStore
{
    Task<StoredTradeWorkflow?> FindByTradeVersionAsync(
        string tradeId,
        int tradeVersion,
        CancellationToken cancellationToken);

    Task AddAsync(
        Trade trade,
        SettlementWorkflow workflow,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StoredTradeWorkflow?> FindByWorkflowIdAsync(
        Guid workflowId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<StoredTradeWorkflow>> ListAsync(CancellationToken cancellationToken);

    Task UpdateWorkflowAsync(
        SettlementWorkflow workflow,
        CancellationToken cancellationToken);

    Task<SettlementRecord?> FindSettlementByWorkflowIdAsync(
        Guid workflowId,
        CancellationToken cancellationToken);

    Task AddSettlementAsync(
        SettlementRecord settlement,
        CancellationToken cancellationToken);

    Task<Invoice?> FindInvoiceBySettlementIdAsync(
        Guid settlementId,
        CancellationToken cancellationToken);

    Task AddInvoiceAsync(
        Invoice invoice,
        CancellationToken cancellationToken);

    Task<PaymentRequest?> FindPaymentRequestByInvoiceIdAsync(
        Guid invoiceId,
        CancellationToken cancellationToken);

    Task AddPaymentRequestAsync(
        PaymentRequest paymentRequest,
        CancellationToken cancellationToken);

    Task AddOutboxMessageAsync(
        OutboxMessage message,
        CancellationToken cancellationToken);
}

public sealed record StoredTradeWorkflow(
    Trade Trade,
    SettlementWorkflow Workflow,
    string IdempotencyKey);
