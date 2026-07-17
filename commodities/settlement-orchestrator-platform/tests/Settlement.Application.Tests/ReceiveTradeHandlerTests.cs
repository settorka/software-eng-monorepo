using Settlement.Application.Common;
using Settlement.Application.Trades;
using Settlement.Infrastructure.Trades;
using Xunit;

namespace Settlement.Application.Tests;

public sealed class ReceiveTradeHandlerTests
{
    private static readonly DateOnly TradeDate = new(2026, 7, 17);
    private static readonly DateOnly SettlementDate = new(2026, 7, 31);

    [Fact]
    public async Task DuplicateTradeWithSamePayloadReturnsExistingWorkflow()
    {
        var handler = new ReceiveTradeHandler(new InMemoryTradeWorkflowStore(), new FixedClock());
        var command = CreateCommand();

        var first = await handler.HandleAsync(command, CancellationToken.None);
        var second = await handler.HandleAsync(command with { IdempotencyKey = "idem-2" }, CancellationToken.None);

        Assert.False(first.WasDuplicate);
        Assert.True(second.WasDuplicate);
        Assert.Equal(first.WorkflowId, second.WorkflowId);
    }

    [Fact]
    public async Task DuplicateTradeWithDifferentPayloadIsRejected()
    {
        var handler = new ReceiveTradeHandler(new InMemoryTradeWorkflowStore(), new FixedClock());
        var command = CreateCommand();

        await handler.HandleAsync(command, CancellationToken.None);

        await Assert.ThrowsAsync<DuplicateTradeConflictException>(() =>
            handler.HandleAsync(command with { Price = 91.25m, IdempotencyKey = "idem-2" }, CancellationToken.None));
    }

    private static ReceiveTradeCommand CreateCommand()
    {
        return new ReceiveTradeCommand(
            "TRD-2026-000001",
            1,
            "POWER",
            "CP-001",
            1_000,
            "MWH",
            85.25m,
            "GBP",
            TradeDate,
            SettlementDate,
            "idem-1",
            "corr-1");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    }
}
