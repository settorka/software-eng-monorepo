using Settlement.Domain.Audit;
using Settlement.Domain.Common;
using Settlement.Domain.Trades;

namespace Settlement.Domain.Workflows;

public sealed class SettlementWorkflow
{
    private readonly List<WorkflowTransition> _transitions = [];
    private readonly List<AuditEvent> _auditEvents = [];

    private SettlementWorkflow(
        Guid workflowId,
        string tradeId,
        int tradeVersion,
        WorkflowState state,
        int workflowVersion,
        DateTimeOffset createdAt)
    {
        WorkflowId = workflowId;
        TradeId = tradeId;
        TradeVersion = tradeVersion;
        State = state;
        WorkflowVersion = workflowVersion;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid WorkflowId { get; }

    public string TradeId { get; }

    public int TradeVersion { get; }

    public WorkflowState State { get; private set; }

    public int WorkflowVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<WorkflowTransition> Transitions => _transitions.AsReadOnly();

    public IReadOnlyCollection<AuditEvent> AuditEvents => _auditEvents.AsReadOnly();

    public static SettlementWorkflow Start(
        Trade trade,
        string correlationId,
        DateTimeOffset now)
    {
        var workflow = new SettlementWorkflow(
            Guid.NewGuid(),
            trade.TradeId,
            trade.TradeVersion,
            WorkflowState.Pending,
            workflowVersion: 1,
            createdAt: now);

        workflow.AppendAudit("WorkflowCreated", correlationId, correlationId, now, "Settlement workflow created.");
        return workflow;
    }

    public void TransitionTo(
        WorkflowState nextState,
        string reason,
        string correlationId,
        string causationId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(causationId);

        if (WorkflowTransitionPolicy.IsTerminal(State))
        {
            throw new DomainException($"Workflow {WorkflowId} is terminal in state {State}.");
        }

        WorkflowTransitionPolicy.EnsureAllowed(State, nextState);

        var previous = State;
        State = nextState;
        WorkflowVersion++;
        UpdatedAt = now;

        _transitions.Add(new WorkflowTransition(previous, nextState, reason));
        AppendAudit("WorkflowTransitioned", correlationId, causationId, now, $"{previous} -> {nextState}: {reason}");
    }

    private void AppendAudit(
        string eventType,
        string correlationId,
        string causationId,
        DateTimeOffset occurredAt,
        string details)
    {
        _auditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            WorkflowId,
            TradeId,
            TradeVersion,
            eventType,
            correlationId,
            causationId,
            occurredAt,
            details));
    }
}

