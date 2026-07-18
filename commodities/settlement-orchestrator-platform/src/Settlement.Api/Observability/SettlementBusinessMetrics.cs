using Prometheus;
using Settlement.Application.Workflows;
using Settlement.Domain.Workflows;

namespace Settlement.Api.Observability;

public sealed class SettlementBusinessMetrics
{
    private static readonly Counter TradesReceived = Metrics.CreateCounter(
        "settlement_trades_received_total",
        "Total confirmed trades accepted by the settlement API.",
        new CounterConfiguration
        {
            LabelNames = ["commodity", "currency", "duplicate"]
        });

    private static readonly Counter WorkflowTransitions = Metrics.CreateCounter(
        "settlement_workflow_transitions_total",
        "Total observed settlement workflow state transitions.",
        new CounterConfiguration
        {
            LabelNames = ["state"]
        });

    private static readonly Gauge WorkflowStates = Metrics.CreateGauge(
        "settlement_workflows_by_state",
        "Current settlement workflow count by state.",
        new GaugeConfiguration
        {
            LabelNames = ["state"]
        });

    private static readonly Histogram CompletionLatency = Metrics.CreateHistogram(
        "settlement_workflow_completion_latency_seconds",
        "Observed workflow completion latency in seconds.",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(1, 2, 12)
        });

    public void RecordTradeAccepted(string commodity, string currency, bool wasDuplicate)
    {
        TradesReceived
            .WithLabels(commodity, currency, wasDuplicate ? "true" : "false")
            .Inc();
    }

    public void RecordWorkflowState(WorkflowState state)
    {
        WorkflowTransitions
            .WithLabels(state.ToString())
            .Inc();
    }

    public void SetWorkflowStateCounts(IEnumerable<WorkflowDetails> workflows)
    {
        var counts = workflows
            .GroupBy(workflow => workflow.State)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var state in Enum.GetValues<WorkflowState>())
        {
            WorkflowStates.WithLabels(state.ToString()).Set(counts.GetValueOrDefault(state));
        }
    }

    public void RecordCompletionLatency(WorkflowDetails workflow, DateTimeOffset now)
    {
        if (workflow.State != WorkflowState.Completed)
        {
            return;
        }

        var firstAudit = workflow.AuditEvents.OrderBy(audit => audit.OccurredAt).FirstOrDefault();
        if (firstAudit is null)
        {
            return;
        }

        var latency = now - firstAudit.OccurredAt;
        if (latency < TimeSpan.Zero)
        {
            return;
        }

        CompletionLatency.Observe(latency.TotalSeconds);
    }
}
