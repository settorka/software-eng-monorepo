using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Settlement.Infrastructure.Persistence;

public sealed class SettlementDatabaseMigrator(
    IServiceProvider serviceProvider,
    IOptions<DatabaseOptions> options,
    ILogger<SettlementDatabaseMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.RunMigrationsOnStartup)
        {
            return;
        }

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SettlementDbContext>();

        logger.LogInformation("Applying settlement database migrations.");
        await dbContext.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Settlement database migrations applied.");
    }
}
