namespace Settlement.Api.Contracts;

public sealed record ReceiveTradeRequest(
    string TradeId,
    int TradeVersion,
    string Commodity,
    string Counterparty,
    decimal Quantity,
    string Unit,
    decimal Price,
    string Currency,
    DateOnly TradeDate,
    DateOnly SettlementDate);

