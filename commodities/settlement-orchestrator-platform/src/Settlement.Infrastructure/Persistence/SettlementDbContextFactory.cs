using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Settlement.Infrastructure.Persistence;

public sealed class SettlementDbContextFactory : IDesignTimeDbContextFactory<SettlementDbContext>
{
    public SettlementDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Oracle") ??
            "User Id=settlement;Password=settlement;Data Source=localhost:1521/FREEPDB1";

        var options = new DbContextOptionsBuilder<SettlementDbContext>()
            .UseOracle(
                connectionString,
                oracleOptions => oracleOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options;

        return new SettlementDbContext(options);
    }
}
