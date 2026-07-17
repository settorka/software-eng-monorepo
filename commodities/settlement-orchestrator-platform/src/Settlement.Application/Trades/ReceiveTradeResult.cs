using Settlement.Domain.Workflows;

namespace Settlement.Application.Trades;

public sealed record ReceiveTradeResult(
    Guid WorkflowId,
    string TradeId,
    int TradeVersion,
    WorkflowState State,
    bool WasDuplicate);

