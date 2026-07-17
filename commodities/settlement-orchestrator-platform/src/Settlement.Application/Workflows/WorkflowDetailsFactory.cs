using Settlement.Application.Trades;

namespace Settlement.Application.Workflows;

public static class WorkflowDetailsFactory
{
    public static async Task<WorkflowDetails> CreateAsync(
        StoredTradeWorkflow stored,
        ITradeWorkflowStore store,
        CancellationToken cancellationToken)
    {
        var workflow = stored.Workflow;
        var settlement = await store.FindSettlementByWorkflowIdAsync(workflow.WorkflowId, cancellationToken);
        var invoice = settlement is null
            ? null
            : await store.FindInvoiceBySettlementIdAsync(settlement.SettlementId, cancellationToken);
        var paymentRequest = invoice is null
            ? null
            : await store.FindPaymentRequestByInvoiceIdAsync(invoice.InvoiceId, cancellationToken);

        return new WorkflowDetails(
            workflow.WorkflowId,
            workflow.TradeId,
            workflow.TradeVersion,
            workflow.State,
            workflow.WorkflowVersion,
            settlement?.SettlementId,
            settlement?.Amount,
            invoice?.InvoiceId,
            paymentRequest?.PaymentRequestId,
            workflow.Transitions,
            workflow.AuditEvents);
    }
}

