using Settlement.Application.Trades;

namespace Settlement.Application.Workflows;

public sealed class ListWorkflowsHandler(ITradeWorkflowStore store)
{
    public async Task<IReadOnlyCollection<WorkflowDetails>> HandleAsync(CancellationToken cancellationToken)
    {
        var stored = await store.ListAsync(cancellationToken);

        var workflows = new List<WorkflowDetails>();

        foreach (var item in stored)
        {
            workflows.Add(await WorkflowDetailsFactory.CreateAsync(item, store, cancellationToken));
        }

        return workflows;
    }
}
