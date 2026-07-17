using Settlement.Application.Common;
using Settlement.Application.Trades;
using Settlement.Domain.Common;
using Settlement.Domain.Invoices;
using Settlement.Domain.Outbox;
using Settlement.Domain.Payments;
using Settlement.Domain.Settlements;
using Settlement.Domain.Workflows;

namespace Settlement.Application.Workflows;

public sealed class ExecuteWorkflowStepHandler(
    ITradeWorkflowStore store,
    IClock clock)
{
    public async Task<WorkflowDetails> HandleAsync(
        Guid workflowId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var stored = await store.FindByWorkflowIdAsync(workflowId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow {workflowId} was not found.");

        var workflow = stored.Workflow;
        var now = clock.UtcNow;

        switch (workflow.State)
        {
            case WorkflowState.Validating:
                workflow.TransitionTo(WorkflowState.Calculating, "Trade passed validation.", correlationId, correlationId, now);

                var existingSettlement = await store.FindSettlementByWorkflowIdAsync(workflow.WorkflowId, cancellationToken);
                if (existingSettlement is null)
                {
                    var settlement = new SettlementRecord(
                        DeterministicGuid.From($"settlement:{workflow.WorkflowId}:{workflow.WorkflowVersion}"),
                        workflow.WorkflowId,
                        stored.Trade.TradeId,
                        stored.Trade.TradeVersion,
                        stored.Trade.Quantity * stored.Trade.Price,
                        stored.Trade.Currency,
                        now);

                    await store.AddSettlementAsync(settlement, cancellationToken);
                }

                workflow.TransitionTo(WorkflowState.AwaitingApproval, "Settlement calculated.", correlationId, correlationId, now);
                break;

            case WorkflowState.Approved:
                workflow.TransitionTo(WorkflowState.InvoiceGenerating, "Approval received.", correlationId, correlationId, now);

                var approvedSettlement = await store.FindSettlementByWorkflowIdAsync(workflow.WorkflowId, cancellationToken)
                    ?? throw new DomainException($"Workflow {workflow.WorkflowId} has no settlement to invoice.");

                var existingInvoice = await store.FindInvoiceBySettlementIdAsync(approvedSettlement.SettlementId, cancellationToken);
                var invoice = existingInvoice;

                if (invoice is null)
                {
                    invoice = new Invoice(
                        DeterministicGuid.From($"invoice:{approvedSettlement.SettlementId}"),
                        approvedSettlement.SettlementId,
                        $"INV-{approvedSettlement.TradeId}-{approvedSettlement.TradeVersion}",
                        now);

                    await store.AddInvoiceAsync(invoice, cancellationToken);
                }

                workflow.TransitionTo(WorkflowState.InvoiceGenerated, "Invoice generated.", correlationId, correlationId, now);
                workflow.TransitionTo(WorkflowState.PaymentPublishing, "Invoice ready for payment request.", correlationId, correlationId, now);

                var existingPaymentRequest = await store.FindPaymentRequestByInvoiceIdAsync(invoice.InvoiceId, cancellationToken);

                if (existingPaymentRequest is null)
                {
                    var paymentRequest = new PaymentRequest(
                        DeterministicGuid.From($"payment-request:{invoice.InvoiceId}"),
                        invoice.InvoiceId,
                        $"payment-request:{invoice.InvoiceId}",
                        now);

                    await store.AddPaymentRequestAsync(paymentRequest, cancellationToken);
                    await store.AddOutboxMessageAsync(
                        new OutboxMessage(
                            DeterministicGuid.From($"outbox:payment-request:{paymentRequest.PaymentRequestId}"),
                            workflow.WorkflowId,
                            "PaymentRequestCreated",
                            paymentRequest.IdempotencyKey,
                            now),
                        cancellationToken);
                }

                workflow.TransitionTo(WorkflowState.PaymentRequested, "Payment request recorded.", correlationId, correlationId, now);
                break;

            case WorkflowState.PaymentRequested:
                workflow.TransitionTo(WorkflowState.Completed, "Payment request lifecycle completed.", correlationId, correlationId, now);
                break;

            default:
                throw new DomainException($"Workflow {workflow.WorkflowId} has no executable step in state {workflow.State}.");
        }

        await store.UpdateWorkflowAsync(workflow, cancellationToken);

        return await WorkflowDetailsFactory.CreateAsync(stored, store, cancellationToken);
    }
}
