namespace Settlement.Application.Trades;

public sealed class DuplicateTradeConflictException(string tradeId, int tradeVersion)
    : Exception($"Trade {tradeId} version {tradeVersion} already exists with a different payload.");

