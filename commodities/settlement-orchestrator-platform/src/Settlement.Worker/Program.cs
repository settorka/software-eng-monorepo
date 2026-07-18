using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Settlement.Application.Common;
using Settlement.Application.Outbox;
using Settlement.Application.Workflows;
using Settlement.Infrastructure;
using Settlement.Infrastructure.Outbox;
using Settlement.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerControlsOptions>(
    builder.Configuration.GetSection(WorkerControlsOptions.SectionName));
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("settlement-worker"))
    .WithTracing(tracing => tracing
        .AddSource(OracleOutboxDispatcher.ActivitySourceName)
        .AddOtlpExporter());
builder.Services.AddSettlementInfrastructure(builder.Configuration);
builder.Services.AddHostedService<SettlementWorker>();

builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

var app = builder.Build();
await app.Services.GetRequiredService<SettlementDatabaseMigrator>().MigrateAsync(CancellationToken.None);
await app.RunAsync();

public sealed class SettlementWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<WorkerControlsOptions> controls,
    ILogger<SettlementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var currentControls = controls.CurrentValue;

            try
            {
                await PumpWorkflowsAsync(currentControls, stoppingToken);
                await DispatchOutboxAsync(currentControls, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Settlement workflow pump failed.");
            }

            await DelayAsync(currentControls, stoppingToken);
        }
    }

    private async Task PumpWorkflowsAsync(WorkerControlsOptions currentControls, CancellationToken stoppingToken)
    {
        if (!currentControls.WorkflowPumpEnabled)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<PumpWorkflowsHandler>();
        var correlationId = $"worker-{Guid.NewGuid():N}";

        var workflows = await handler.HandleAsync(
            correlationId,
            currentControls.MaxPumpWorkflows,
            stoppingToken);

        if (workflows.Count > 0)
        {
            logger.LogInformation("Pumped {WorkflowCount} settlement workflows.", workflows.Count);
        }
    }

    private async Task DispatchOutboxAsync(WorkerControlsOptions currentControls, CancellationToken stoppingToken)
    {
        if (!currentControls.OutboxDispatcherEnabled)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

        var published = await dispatcher.DispatchAsync(
            currentControls.OutboxBatchSize,
            currentControls.OutboxMaxAttempts,
            stoppingToken);

        if (published > 0)
        {
            logger.LogInformation("Published {OutboxMessageCount} outbox messages.", published);
        }
    }

    private static Task DelayAsync(WorkerControlsOptions controls, CancellationToken cancellationToken)
    {
        var interval = Math.Max(100, controls.PollIntervalMilliseconds);
        return Task.Delay(interval, cancellationToken);
    }
}
