using Microsoft.EntityFrameworkCore;
using Settlement.Infrastructure.Persistence.Entities;

namespace Settlement.Infrastructure.Persistence;

public sealed class SettlementDbContext(DbContextOptions<SettlementDbContext> options) : DbContext(options)
{
    public DbSet<TradeEntity> Trades => Set<TradeEntity>();

    public DbSet<WorkflowEntity> Workflows => Set<WorkflowEntity>();

    public DbSet<WorkflowTransitionEntity> WorkflowTransitions => Set<WorkflowTransitionEntity>();

    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    public DbSet<SettlementEntity> Settlements => Set<SettlementEntity>();

    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();

    public DbSet<PaymentRequestEntity> PaymentRequests => Set<PaymentRequestEntity>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TradeEntity>(entity =>
        {
            entity.ToTable("TRADES");
            entity.HasKey(trade => new { trade.TradeId, trade.TradeVersion });
            entity.Property(trade => trade.TradeId).HasMaxLength(64);
            entity.Property(trade => trade.Commodity).HasMaxLength(32);
            entity.Property(trade => trade.Counterparty).HasMaxLength(128);
            entity.Property(trade => trade.Unit).HasMaxLength(16);
            entity.Property(trade => trade.Currency).HasMaxLength(3);
            entity.Property(trade => trade.PayloadHash).HasMaxLength(128);
            entity.Property(trade => trade.Quantity).HasPrecision(24, 8);
            entity.Property(trade => trade.Price).HasPrecision(24, 8);
        });

        modelBuilder.Entity<WorkflowEntity>(entity =>
        {
            entity.ToTable("SETTLEMENT_WORKFLOWS");
            entity.HasKey(workflow => workflow.WorkflowId);
            entity.HasIndex(workflow => new { workflow.TradeId, workflow.TradeVersion }).IsUnique();
            entity.Property(workflow => workflow.TradeId).HasMaxLength(64);
            entity.Property(workflow => workflow.State).HasMaxLength(32);
            entity.Property(workflow => workflow.IdempotencyKey).HasMaxLength(128);
            entity.Property(workflow => workflow.WorkflowVersion).IsConcurrencyToken();
            entity.HasOne(workflow => workflow.Trade)
                .WithOne(trade => trade.Workflow)
                .HasForeignKey<WorkflowEntity>(workflow => new { workflow.TradeId, workflow.TradeVersion });
        });

        modelBuilder.Entity<WorkflowTransitionEntity>(entity =>
        {
            entity.ToTable("WORKFLOW_TRANSITIONS");
            entity.HasKey(transition => transition.WorkflowTransitionId);
            entity.HasIndex(transition => new { transition.WorkflowId, transition.Sequence }).IsUnique();
            entity.Property(transition => transition.FromState).HasMaxLength(32);
            entity.Property(transition => transition.ToState).HasMaxLength(32);
            entity.Property(transition => transition.Reason).HasMaxLength(512);
            entity.HasOne(transition => transition.Workflow)
                .WithMany(workflow => workflow.Transitions)
                .HasForeignKey(transition => transition.WorkflowId);
        });

        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.ToTable("AUDIT_EVENTS");
            entity.HasKey(audit => audit.AuditEventId);
            entity.HasIndex(audit => new { audit.TradeId, audit.TradeVersion, audit.OccurredAt });
            entity.Property(audit => audit.TradeId).HasMaxLength(64);
            entity.Property(audit => audit.EventType).HasMaxLength(64);
            entity.Property(audit => audit.CorrelationId).HasMaxLength(128);
            entity.Property(audit => audit.CausationId).HasMaxLength(128);
            entity.Property(audit => audit.Details).HasMaxLength(1024);
            entity.HasOne(audit => audit.Workflow)
                .WithMany(workflow => workflow.AuditEvents)
                .HasForeignKey(audit => audit.WorkflowId);
        });

        modelBuilder.Entity<SettlementEntity>(entity =>
        {
            entity.ToTable("SETTLEMENTS");
            entity.HasKey(settlement => settlement.SettlementId);
            entity.HasIndex(settlement => settlement.WorkflowId).IsUnique();
            entity.Property(settlement => settlement.TradeId).HasMaxLength(64);
            entity.Property(settlement => settlement.Amount).HasPrecision(24, 8);
            entity.Property(settlement => settlement.Currency).HasMaxLength(3);
            entity.HasOne(settlement => settlement.Workflow)
                .WithOne(workflow => workflow.Settlement)
                .HasForeignKey<SettlementEntity>(settlement => settlement.WorkflowId);
        });

        modelBuilder.Entity<InvoiceEntity>(entity =>
        {
            entity.ToTable("INVOICES");
            entity.HasKey(invoice => invoice.InvoiceId);
            entity.HasIndex(invoice => invoice.SettlementId).IsUnique();
            entity.HasIndex(invoice => invoice.InvoiceNumber).IsUnique();
            entity.Property(invoice => invoice.InvoiceNumber).HasMaxLength(128);
            entity.HasOne(invoice => invoice.Settlement)
                .WithOne(settlement => settlement.Invoice)
                .HasForeignKey<InvoiceEntity>(invoice => invoice.SettlementId);
        });

        modelBuilder.Entity<PaymentRequestEntity>(entity =>
        {
            entity.ToTable("PAYMENT_REQUESTS");
            entity.HasKey(paymentRequest => paymentRequest.PaymentRequestId);
            entity.HasIndex(paymentRequest => paymentRequest.InvoiceId).IsUnique();
            entity.HasIndex(paymentRequest => paymentRequest.IdempotencyKey).IsUnique();
            entity.Property(paymentRequest => paymentRequest.IdempotencyKey).HasMaxLength(128);
            entity.HasOne(paymentRequest => paymentRequest.Invoice)
                .WithOne(invoice => invoice.PaymentRequest)
                .HasForeignKey<PaymentRequestEntity>(paymentRequest => paymentRequest.InvoiceId);
        });

        modelBuilder.Entity<OutboxMessageEntity>(entity =>
        {
            entity.ToTable("OUTBOX_MESSAGES");
            entity.HasKey(message => message.OutboxMessageId);
            entity.HasIndex(message => new { message.MessageType, message.Payload }).IsUnique();
            entity.HasIndex(message => new { message.PublishedAt, message.NextAttemptAt });
            entity.Property(message => message.MessageType).HasMaxLength(128);
            entity.Property(message => message.Payload).HasMaxLength(4000);
            entity.Property(message => message.LastError).HasMaxLength(1024);
        });
    }
}
