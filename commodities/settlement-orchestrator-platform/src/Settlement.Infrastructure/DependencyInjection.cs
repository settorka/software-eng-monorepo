using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Settlement.Application.Common;
using Settlement.Application.Trades;
using Settlement.Infrastructure.Trades;

namespace Settlement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSettlementInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITradeWorkflowStore, InMemoryTradeWorkflowStore>();
        services.AddScoped<ReceiveTradeHandler>();

        return services;
    }
}

