using Settlement.Application.Trades;

namespace Settlement.Application.Workflows;

public sealed class GetWorkflowHandler(ITradeWorkflowStore store)
{
    public async Task<WorkflowDetails?> HandleAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var stored = await store.FindByWorkflowIdAsync(workflowId, cancellationToken);

        if (stored is null)
        {
            return null;
        }

        return await WorkflowDetailsFactory.CreateAsync(stored, store, cancellationToken);
    }
}
