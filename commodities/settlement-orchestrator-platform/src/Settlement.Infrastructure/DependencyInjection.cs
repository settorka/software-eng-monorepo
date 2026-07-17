using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Settlement.Application.Common;
using Settlement.Application.Trades;
using Settlement.Application.Workflows;
using Settlement.Infrastructure.Persistence;
using Settlement.Infrastructure.Trades;

namespace Settlement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSettlementInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();

        var oracleConnectionString = configuration.GetConnectionString("Oracle");
        if (string.IsNullOrWhiteSpace(oracleConnectionString))
        {
            services.AddSingleton<ITradeWorkflowStore, InMemoryTradeWorkflowStore>();
        }
        else
        {
            services.AddDbContext<SettlementDbContext>(options => options.UseOracle(oracleConnectionString));
            services.AddScoped<ITradeWorkflowStore, OracleTradeWorkflowStore>();
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
