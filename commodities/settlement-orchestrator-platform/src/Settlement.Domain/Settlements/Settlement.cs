namespace Settlement.Domain.Settlements;

public sealed record Settlement(
    Guid SettlementId,
    Guid WorkflowId,
    string TradeId,
    int TradeVersion,
    decimal Amount,
    string Currency,
    DateTimeOffset CalculatedAt);

