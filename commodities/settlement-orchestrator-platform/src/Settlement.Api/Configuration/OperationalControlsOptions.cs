namespace Settlement.Api.Configuration;

public sealed class OperationalControlsOptions
{
    public const string SectionName = "OperationalControls";

    public bool IntakeEnabled { get; init; } = true;

    public bool WorkflowPumpEnabled { get; init; } = true;

    public int MaxPumpWorkflows { get; init; } = 25;

    public int MaxRequestBodyBytes { get; init; } = 65_536;

    public bool DetailedErrors { get; init; }
}

