using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Settlement.Application.Common;
using Settlement.Application.Trades;
using Settlement.Application.Workflows;
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
        services.AddScoped<GetWorkflowHandler>();
        services.AddScoped<ListWorkflowsHandler>();
        services.AddScoped<ExecuteWorkflowStepHandler>();
        services.AddScoped<ApproveWorkflowHandler>();
        services.AddScoped<RetryWorkflowHandler>();

        return services;
    }
}
