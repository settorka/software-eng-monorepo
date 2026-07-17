using Settlement.Domain.Common;
using Settlement.Domain.Workflows;

namespace Settlement.Application.Workflows;

public sealed class PumpWorkflowsHandler(
    ListWorkflowsHandler listWorkflows,
    ExecuteWorkflowStepHandler executeWorkflowStep)
{
    private static readonly IReadOnlySet<WorkflowState> ExecutableStates =
        new HashSet<WorkflowState>
        {
            WorkflowState.Validating,
            WorkflowState.Approved,
            WorkflowState.PaymentRequested
        };

    public async Task<IReadOnlyCollection<WorkflowDetails>> HandleAsync(
        string correlationId,
        int maxWorkflows,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (maxWorkflows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWorkflows), "Maximum workflow count must be positive.");
        }

        var workflows = await listWorkflows.HandleAsync(cancellationToken);
        var executed = new List<WorkflowDetails>();

        foreach (var workflow in workflows.Where(workflow => ExecutableStates.Contains(workflow.State)).Take(maxWorkflows))
        {
            try
            {
                executed.Add(await executeWorkflowStep.HandleAsync(workflow.WorkflowId, correlationId, cancellationToken));
            }
            catch (DomainException)
            {
                // Another caller may have advanced the workflow. The durable implementation will use row-version checks.
            }
        }

        return executed;
    }
}
