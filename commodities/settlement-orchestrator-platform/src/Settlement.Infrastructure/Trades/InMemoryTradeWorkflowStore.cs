using System.Collections.Concurrent;
using Settlement.Application.Trades;
using Settlement.Domain.Invoices;
using Settlement.Domain.Outbox;
using Settlement.Domain.Payments;
using Settlement.Domain.Settlements;
using Settlement.Domain.Trades;
using Settlement.Domain.Workflows;

namespace Settlement.Infrastructure.Trades;

public sealed class InMemoryTradeWorkflowStore : ITradeWorkflowStore
{
    private readonly ConcurrentDictionary<(string TradeId, int TradeVersion), StoredTradeWorkflow> _workflows = [];
    private readonly ConcurrentDictionary<Guid, (string TradeId, int TradeVersion)> _workflowIndex = [];
    private readonly ConcurrentDictionary<Guid, SettlementRecord> _settlementsByWorkflow = [];
    private readonly ConcurrentDictionary<Guid, Invoice> _invoicesBySettlement = [];
    private readonly ConcurrentDictionary<Guid, PaymentRequest> _paymentRequestsByInvoice = [];
    private readonly ConcurrentDictionary<Guid, OutboxMessage> _outboxMessages = [];

    public Task<StoredTradeWorkflow?> FindByTradeVersionAsync(
        string tradeId,
        int tradeVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _workflows.TryGetValue((tradeId, tradeVersion), out var workflow);
        return Task.FromResult(workflow);
    }

    public Task AddAsync(
        Trade trade,
        SettlementWorkflow workflow,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stored = new StoredTradeWorkflow(trade, workflow, idempotencyKey);

        if (!_workflows.TryAdd((trade.TradeId, trade.TradeVersion), stored))
        {
            throw new InvalidOperationException($"Trade {trade.TradeId} version {trade.TradeVersion} already exists.");
        }

        _workflowIndex[workflow.WorkflowId] = (trade.TradeId, trade.TradeVersion);

        return Task.CompletedTask;
    }

    public Task<StoredTradeWorkflow?> FindByWorkflowIdAsync(
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_workflowIndex.TryGetValue(workflowId, out var key))
        {
            return Task.FromResult<StoredTradeWorkflow?>(null);
        }

        _workflows.TryGetValue(key, out var workflow);
        return Task.FromResult(workflow);
    }

    public Task<IReadOnlyCollection<StoredTradeWorkflow>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyCollection<StoredTradeWorkflow> workflows = _workflows.Values
            .OrderBy(workflow => workflow.Workflow.CreatedAt)
            .ToArray();

        return Task.FromResult(workflows);
    }

    public Task UpdateWorkflowAsync(
        SettlementWorkflow workflow,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_workflowIndex.TryGetValue(workflow.WorkflowId, out var key))
        {
            throw new InvalidOperationException($"Workflow {workflow.WorkflowId} was not found.");
        }

        if (!_workflows.TryGetValue(key, out var stored))
        {
            throw new InvalidOperationException($"Workflow {workflow.WorkflowId} was not found.");
        }

        _workflows[key] = stored with { Workflow = workflow };
        return Task.CompletedTask;
    }

    public Task<SettlementRecord?> FindSettlementByWorkflowIdAsync(
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _settlementsByWorkflow.TryGetValue(workflowId, out var settlement);
        return Task.FromResult(settlement);
    }

    public Task AddSettlementAsync(
        SettlementRecord settlement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _settlementsByWorkflow.TryAdd(settlement.WorkflowId, settlement);
        return Task.CompletedTask;
    }

    public Task<Invoice?> FindInvoiceBySettlementIdAsync(
        Guid settlementId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _invoicesBySettlement.TryGetValue(settlementId, out var invoice);
        return Task.FromResult(invoice);
    }

    public Task AddInvoiceAsync(
        Invoice invoice,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _invoicesBySettlement.TryAdd(invoice.SettlementId, invoice);
        return Task.CompletedTask;
    }

    public Task<PaymentRequest?> FindPaymentRequestByInvoiceIdAsync(
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _paymentRequestsByInvoice.TryGetValue(invoiceId, out var paymentRequest);
        return Task.FromResult(paymentRequest);
    }

    public Task AddPaymentRequestAsync(
        PaymentRequest paymentRequest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _paymentRequestsByInvoice.TryAdd(paymentRequest.InvoiceId, paymentRequest);
        return Task.CompletedTask;
    }

    public Task AddOutboxMessageAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _outboxMessages.TryAdd(message.OutboxMessageId, message);
        return Task.CompletedTask;
    }
}
