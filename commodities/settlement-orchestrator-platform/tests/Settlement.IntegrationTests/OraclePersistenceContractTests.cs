using Microsoft.EntityFrameworkCore;
using Settlement.Application.Common;
using Settlement.Application.Trades;
using Settlement.Infrastructure.Persistence;
using Settlement.Infrastructure.Trades;
using Xunit;

namespace Settlement.IntegrationTests;

public sealed class OraclePersistenceContractTests
{
    [Fact]
    public async Task OracleStorePersistsAndReloadsTradeWorkflow()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Oracle");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<SettlementDbContext>()
            .UseOracle(connectionString)
            .Options;

        await using var dbContext = new SettlementDbContext(options);
        await dbContext.Database.MigrateAsync();

        var store = new OracleTradeWorkflowStore(dbContext);
        var handler = new ReceiveTradeHandler(store, new FixedClock());
        var tradeId = $"TRD-IT-{Guid.NewGuid():N}";

        var result = await handler.HandleAsync(
            new ReceiveTradeCommand(
                tradeId,
                1,
                "POWER",
                "CP-IT",
                10,
                "MWH",
                42.25m,
                "GBP",
                new DateOnly(2026, 7, 18),
                new DateOnly(2026, 7, 31),
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N")),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var reloaded = await store.FindByWorkflowIdAsync(result.WorkflowId, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(tradeId, reloaded.Trade.TradeId);
        Assert.Equal(result.WorkflowId, reloaded.Workflow.WorkflowId);
        Assert.Equal(result.State, reloaded.Workflow.State);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);
    }
}
