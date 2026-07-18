namespace Settlement.Application.Common;

public sealed class WorkerControlsOptions
{
    public const string SectionName = "OperationalControls";

    public bool WorkflowPumpEnabled { get; init; } = true;

    public bool OutboxDispatcherEnabled { get; init; } = true;

    public int MaxPumpWorkflows { get; init; } = 25;

    public int OutboxBatchSize { get; init; } = 25;

    public int OutboxMaxAttempts { get; init; } = 10;

    public int PollIntervalMilliseconds { get; init; } = 1_000;
}
