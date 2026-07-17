using Microsoft.EntityFrameworkCore;
using Settlement.Application.Trades;
using Settlement.Domain.Audit;
using Settlement.Domain.Invoices;
using Settlement.Domain.Outbox;
using Settlement.Domain.Payments;
using Settlement.Domain.Settlements;
using Settlement.Domain.Trades;
using Settlement.Domain.Workflows;
using Settlement.Infrastructure.Persistence;
using Settlement.Infrastructure.Persistence.Entities;

namespace Settlement.Infrastructure.Trades;

public sealed class OracleTradeWorkflowStore(SettlementDbContext dbContext) : ITradeWorkflowStore
{
    public async Task<StoredTradeWorkflow?> FindByTradeVersionAsync(
        string tradeId,
        int tradeVersion,
        CancellationToken cancellationToken)
    {
        var workflow = await LoadWorkflowQuery()
            .SingleOrDefaultAsync(
                item => item.TradeId == tradeId && item.TradeVersion == tradeVersion,
                cancellationToken);

        return workflow is null ? null : ToStored(workflow);
    }

    public async Task AddAsync(
        Trade trade,
        SettlementWorkflow workflow,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Trades.Add(ToEntity(trade));
        dbContext.Workflows.Add(ToEntity(workflow, idempotencyKey));
        dbContext.WorkflowTransitions.AddRange(ToTransitionEntities(workflow));
        dbContext.AuditEvents.AddRange(workflow.AuditEvents.Select(ToEntity));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<StoredTradeWorkflow?> FindByWorkflowIdAsync(
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        var workflow = await LoadWorkflowQuery()
            .SingleOrDefaultAsync(item => item.WorkflowId == workflowId, cancellationToken);

        return workflow is null ? null : ToStored(workflow);
    }

    public async Task<IReadOnlyCollection<StoredTradeWorkflow>> ListAsync(CancellationToken cancellationToken)
    {
        var workflows = await LoadWorkflowQuery()
            .OrderBy(workflow => workflow.CreatedAt)
            .Take(250)
            .ToListAsync(cancellationToken);

        return workflows.Select(ToStored).ToArray();
    }

    public async Task UpdateWorkflowAsync(
        SettlementWorkflow workflow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var entity = await dbContext.Workflows
            .Include(item => item.Transitions)
            .Include(item => item.AuditEvents)
            .SingleOrDefaultAsync(item => item.WorkflowId == workflow.WorkflowId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow {workflow.WorkflowId} was not found.");

        entity.State = workflow.State.ToString();
        entity.WorkflowVersion = workflow.WorkflowVersion;
        entity.UpdatedAt = workflow.UpdatedAt;

        var existingTransitionCount = entity.Transitions.Count;
        dbContext.WorkflowTransitions.AddRange(ToTransitionEntities(workflow).Where(item => item.Sequence > existingTransitionCount));

        var existingAuditIds = entity.AuditEvents.Select(item => item.AuditEventId).ToHashSet();
        dbContext.AuditEvents.AddRange(workflow.AuditEvents.Where(audit => !existingAuditIds.Contains(audit.AuditEventId)).Select(ToEntity));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SettlementRecord?> FindSettlementByWorkflowIdAsync(
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.Settlements
            .AsNoTracking()
            .SingleOrDefaultAsync(settlement => settlement.WorkflowId == workflowId, cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task AddSettlementAsync(
        SettlementRecord settlement,
        CancellationToken cancellationToken)
    {
        dbContext.Settlements.Add(ToEntity(settlement));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Invoice?> FindInvoiceBySettlementIdAsync(
        Guid settlementId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(invoice => invoice.SettlementId == settlementId, cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task AddInvoiceAsync(
        Invoice invoice,
        CancellationToken cancellationToken)
    {
        dbContext.Invoices.Add(ToEntity(invoice));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PaymentRequest?> FindPaymentRequestByInvoiceIdAsync(
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.PaymentRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(paymentRequest => paymentRequest.InvoiceId == invoiceId, cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task AddPaymentRequestAsync(
        PaymentRequest paymentRequest,
        CancellationToken cancellationToken)
    {
        dbContext.PaymentRequests.Add(ToEntity(paymentRequest));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddOutboxMessageAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        dbContext.OutboxMessages.Add(ToEntity(message));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<WorkflowEntity> LoadWorkflowQuery()
    {
        return dbContext.Workflows
            .AsNoTracking()
            .Include(workflow => workflow.Trade)
            .Include(workflow => workflow.Transitions)
            .Include(workflow => workflow.AuditEvents);
    }

    private static StoredTradeWorkflow ToStored(WorkflowEntity entity)
    {
        if (entity.Trade is null)
        {
            throw new InvalidOperationException($"Workflow {entity.WorkflowId} has no trade row.");
        }

        var workflow = SettlementWorkflow.Rehydrate(
            entity.WorkflowId,
            entity.TradeId,
            entity.TradeVersion,
            Enum.Parse<WorkflowState>(entity.State),
            entity.WorkflowVersion,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.Transitions.OrderBy(item => item.Sequence).Select(ToDomain),
            entity.AuditEvents.OrderBy(item => item.OccurredAt).Select(ToDomain));

        return new StoredTradeWorkflow(ToDomain(entity.Trade), workflow, entity.IdempotencyKey);
    }

    private static Trade ToDomain(TradeEntity entity)
    {
        return new Trade(
            entity.TradeId,
            entity.TradeVersion,
            entity.Commodity,
            entity.Counterparty,
            entity.Quantity,
            entity.Unit,
            entity.Price,
            entity.Currency,
            DateOnly.FromDateTime(entity.TradeDate),
            DateOnly.FromDateTime(entity.SettlementDate),
            entity.PayloadHash);
    }

    private static WorkflowTransition ToDomain(WorkflowTransitionEntity entity)
    {
        return new WorkflowTransition(
            Enum.Parse<WorkflowState>(entity.FromState),
            Enum.Parse<WorkflowState>(entity.ToState),
            entity.Reason);
    }

    private static AuditEvent ToDomain(AuditEventEntity entity)
    {
        return new AuditEvent(
            entity.AuditEventId,
            entity.WorkflowId,
            entity.TradeId,
            entity.TradeVersion,
            entity.EventType,
            entity.CorrelationId,
            entity.CausationId,
            entity.OccurredAt,
            entity.Details);
    }

    private static SettlementRecord ToDomain(SettlementEntity entity)
    {
        return new SettlementRecord(
            entity.SettlementId,
            entity.WorkflowId,
            entity.TradeId,
            entity.TradeVersion,
            entity.Amount,
            entity.Currency,
            entity.CalculatedAt);
    }

    private static Invoice ToDomain(InvoiceEntity entity)
    {
        return new Invoice(entity.InvoiceId, entity.SettlementId, entity.InvoiceNumber, entity.GeneratedAt);
    }

    private static PaymentRequest ToDomain(PaymentRequestEntity entity)
    {
        return new PaymentRequest(entity.PaymentRequestId, entity.InvoiceId, entity.IdempotencyKey, entity.RequestedAt);
    }

    private static TradeEntity ToEntity(Trade trade)
    {
        return new TradeEntity
        {
            TradeId = trade.TradeId,
            TradeVersion = trade.TradeVersion,
            Commodity = trade.Commodity,
            Counterparty = trade.Counterparty,
            Quantity = trade.Quantity,
            Unit = trade.Unit,
            Price = trade.Price,
            Currency = trade.Currency,
            TradeDate = trade.TradeDate.ToDateTime(TimeOnly.MinValue),
            SettlementDate = trade.SettlementDate.ToDateTime(TimeOnly.MinValue),
            PayloadHash = trade.PayloadHash
        };
    }

    private static WorkflowEntity ToEntity(SettlementWorkflow workflow, string idempotencyKey)
    {
        return new WorkflowEntity
        {
            WorkflowId = workflow.WorkflowId,
            TradeId = workflow.TradeId,
            TradeVersion = workflow.TradeVersion,
            State = workflow.State.ToString(),
            WorkflowVersion = workflow.WorkflowVersion,
            IdempotencyKey = idempotencyKey,
            CreatedAt = workflow.CreatedAt,
            UpdatedAt = workflow.UpdatedAt
        };
    }

    private static IEnumerable<WorkflowTransitionEntity> ToTransitionEntities(SettlementWorkflow workflow)
    {
        return workflow.Transitions.Select((transition, index) => new WorkflowTransitionEntity
        {
            WorkflowId = workflow.WorkflowId,
            Sequence = index + 1,
            FromState = transition.From.ToString(),
            ToState = transition.To.ToString(),
            Reason = transition.Reason
        });
    }

    private static AuditEventEntity ToEntity(AuditEvent audit)
    {
        return new AuditEventEntity
        {
            AuditEventId = audit.AuditEventId,
            WorkflowId = audit.WorkflowId,
            TradeId = audit.TradeId,
            TradeVersion = audit.TradeVersion,
            EventType = audit.EventType,
            CorrelationId = audit.CorrelationId,
            CausationId = audit.CausationId,
            OccurredAt = audit.OccurredAt,
            Details = audit.Details
        };
    }

    private static SettlementEntity ToEntity(SettlementRecord settlement)
    {
        return new SettlementEntity
        {
            SettlementId = settlement.SettlementId,
            WorkflowId = settlement.WorkflowId,
            TradeId = settlement.TradeId,
            TradeVersion = settlement.TradeVersion,
            Amount = settlement.Amount,
            Currency = settlement.Currency,
            CalculatedAt = settlement.CalculatedAt
        };
    }

    private static InvoiceEntity ToEntity(Invoice invoice)
    {
        return new InvoiceEntity
        {
            InvoiceId = invoice.InvoiceId,
            SettlementId = invoice.SettlementId,
            InvoiceNumber = invoice.InvoiceNumber,
            GeneratedAt = invoice.GeneratedAt
        };
    }

    private static PaymentRequestEntity ToEntity(PaymentRequest paymentRequest)
    {
        return new PaymentRequestEntity
        {
            PaymentRequestId = paymentRequest.PaymentRequestId,
            InvoiceId = paymentRequest.InvoiceId,
            IdempotencyKey = paymentRequest.IdempotencyKey,
            RequestedAt = paymentRequest.RequestedAt
        };
    }

    private static OutboxMessageEntity ToEntity(OutboxMessage message)
    {
        return new OutboxMessageEntity
        {
            OutboxMessageId = message.OutboxMessageId,
            WorkflowId = message.WorkflowId,
            MessageType = message.MessageType,
            Payload = message.Payload,
            CreatedAt = message.CreatedAt,
            PublishedAt = message.PublishedAt,
            NextAttemptAt = message.CreatedAt
        };
    }
}
