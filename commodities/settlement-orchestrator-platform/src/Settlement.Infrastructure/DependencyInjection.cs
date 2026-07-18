using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Settlement.Application.Common;
using Settlement.Application.Outbox;
using Settlement.Application.Trades;
using Settlement.Application.Workflows;
using Settlement.Infrastructure.Outbox;
using Settlement.Infrastructure.Persistence;
using Settlement.Infrastructure.Trades;

namespace Settlement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSettlementInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<OutboxPublisherOptions>(configuration.GetSection(OutboxPublisherOptions.SectionName));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<SettlementDatabaseMigrator>();

        var oracleConnectionString = configuration.GetConnectionString("Oracle");
        if (string.IsNullOrWhiteSpace(oracleConnectionString))
        {
            services.AddSingleton<ITradeWorkflowStore, InMemoryTradeWorkflowStore>();
            services.AddSingleton<IOutboxDispatcher, NoopOutboxDispatcher>();
        }
        else
        {
            services.AddDbContext<SettlementDbContext>(options => options.UseOracle(
                oracleConnectionString,
                oracleOptions => oracleOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
            services.AddScoped<ITradeWorkflowStore, OracleTradeWorkflowStore>();
            services.AddScoped<LoggingOutboxPublisher>();
            services.AddHttpClient<HttpOutboxPublisher>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxPublisherOptions>>()
                    .Value;
                httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Max(100, options.TimeoutMilliseconds));
            });
            services.AddScoped<IOutboxPublisher>(serviceProvider =>
            {
                var options = serviceProvider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxPublisherOptions>>()
                    .Value;

                return options.Mode.Equals("Http", StringComparison.OrdinalIgnoreCase)
                    ? serviceProvider.GetRequiredService<HttpOutboxPublisher>()
                    : serviceProvider.GetRequiredService<LoggingOutboxPublisher>();
            });
            services.AddScoped<IOutboxDispatcher, OracleOutboxDispatcher>();
        }

        services.AddScoped<ReceiveTradeHandler>();
        services.AddScoped<GetWorkflowHandler>();
        services.AddScoped<ListWorkflowsHandler>();
        services.AddScoped<ExecuteWorkflowStepHandler>();
        services.AddScoped<ApproveWorkflowHandler>();
        services.AddScoped<RetryWorkflowHandler>();
        services.AddScoped<PumpWorkflowsHandler>();

        return services;
    }
}
