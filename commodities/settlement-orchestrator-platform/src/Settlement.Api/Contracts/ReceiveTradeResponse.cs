using Settlement.Domain.Workflows;

namespace Settlement.Api.Contracts;

public sealed record ReceiveTradeResponse(
    Guid WorkflowId,
    string TradeId,
    int TradeVersion,
    WorkflowState State,
    bool WasDuplicate);

