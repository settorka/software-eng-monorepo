namespace Settlement.Application.Trades;

public sealed record ReceiveTradeCommand(
    string TradeId,
    int TradeVersion,
    string Commodity,
    string Counterparty,
    decimal Quantity,
    string Unit,
    decimal Price,
    string Currency,
    DateOnly TradeDate,
    DateOnly SettlementDate,
    string IdempotencyKey,
    string CorrelationId);

