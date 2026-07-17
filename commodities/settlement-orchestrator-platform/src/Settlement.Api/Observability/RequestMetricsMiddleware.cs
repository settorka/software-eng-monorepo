using System.Diagnostics;

namespace Settlement.Api.Observability;

public sealed class RequestMetricsMiddleware(
    RequestDelegate next,
    RequestMetrics metrics)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            var route = context.GetEndpoint()?.DisplayName ??
                context.Request.Path.Value ??
                "unknown";

            metrics.Record(
                context.Request.Method,
                route,
                context.Response.StatusCode,
                stopwatch.Elapsed);
        }
    }
}

