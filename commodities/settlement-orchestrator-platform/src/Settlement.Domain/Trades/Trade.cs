namespace Settlement.Domain.Trades;

public sealed record Trade(
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
    string PayloadHash)
{
    public static Trade Create(
        string tradeId,
        int tradeVersion,
        string commodity,
        string counterparty,
        decimal quantity,
        string unit,
        decimal price,
        string currency,
        DateOnly tradeDate,
        DateOnly settlementDate,
        string payloadHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tradeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commodity);
        ArgumentException.ThrowIfNullOrWhiteSpace(counterparty);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        if (tradeVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tradeVersion), "Trade version must be positive.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
        }

        return new Trade(
            tradeId,
            tradeVersion,
            commodity,
            counterparty,
            quantity,
            unit,
            price,
            currency,
            tradeDate,
            settlementDate,
            payloadHash);
    }
}

