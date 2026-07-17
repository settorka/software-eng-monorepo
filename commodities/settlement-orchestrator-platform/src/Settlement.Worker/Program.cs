using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Settlement.Application.Common;
using Settlement.Application.Workflows;
using Settlement.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerControlsOptions>(
    builder.Configuration.GetSection(WorkerControlsOptions.SectionName));
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("settlement-worker"))
    .WithTracing(tracing => tracing.AddOtlpExporter());
builder.Services.AddSettlementInfrastructure(builder.Configuration);
builder.Services.AddHostedService<SettlementWorkflowWorker>();

builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

await builder.Build().RunAsync();

public sealed class SettlementWorkflowWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<WorkerControlsOptions> controls,
    ILogger<SettlementWorkflowWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var currentControls = controls.CurrentValue;

            if (!currentControls.WorkflowPumpEnabled)
            {
                await DelayAsync(currentControls, stoppingToken);
                continue;
            }

            try
            {
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

    private static Task DelayAsync(WorkerControlsOptions controls, CancellationToken cancellationToken)
    {
        var interval = Math.Max(100, controls.PollIntervalMilliseconds);
        return Task.Delay(interval, cancellationToken);
    }
}
