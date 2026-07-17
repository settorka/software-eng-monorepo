using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Settlement.Api.Observability;

public sealed class RequestMetrics
{
    private readonly ConcurrentDictionary<string, RouteMetrics> _routes = [];

    public void Record(string method, string route, int statusCode, TimeSpan duration)
    {
        var key = $"{method} {route} {statusCode}";
        var routeMetrics = _routes.GetOrAdd(key, _ => new RouteMetrics(method, route, statusCode));
        routeMetrics.Record(duration);
    }

    public string ToPrometheus()
    {
        var builder = new StringBuilder();

        builder.AppendLine("# HELP settlement_api_requests_total Total API requests.");
        builder.AppendLine("# TYPE settlement_api_requests_total counter");
        builder.AppendLine("# HELP settlement_api_request_duration_seconds_total Total API request duration in seconds.");
        builder.AppendLine("# TYPE settlement_api_request_duration_seconds_total counter");
        builder.AppendLine("# HELP settlement_api_request_duration_seconds_max Maximum observed API request duration in seconds.");
        builder.AppendLine("# TYPE settlement_api_request_duration_seconds_max gauge");

        foreach (var metrics in _routes.Values.OrderBy(value => value.Route).ThenBy(value => value.StatusCode))
        {
            var labels = $"method=\"{Escape(metrics.Method)}\",route=\"{Escape(metrics.Route)}\",status=\"{metrics.StatusCode}\"";

            builder.Append("settlement_api_requests_total{")
                .Append(labels)
                .Append("} ")
                .AppendLine(metrics.Count.ToString(CultureInfo.InvariantCulture));

            builder.Append("settlement_api_request_duration_seconds_total{")
                .Append(labels)
                .Append("} ")
                .AppendLine(metrics.TotalDurationSeconds.ToString("0.########", CultureInfo.InvariantCulture));

            builder.Append("settlement_api_request_duration_seconds_max{")
                .Append(labels)
                .Append("} ")
                .AppendLine(metrics.MaxDurationSeconds.ToString("0.########", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}

public sealed class RouteMetrics(string method, string route, int statusCode)
{
    private long _count;
    private long _totalTicks;
    private long _maxTicks;

    public string Method { get; } = method;

    public string Route { get; } = route;

    public int StatusCode { get; } = statusCode;

    public long Count => Interlocked.Read(ref _count);

    public double TotalDurationSeconds => TimeSpan.FromTicks(Interlocked.Read(ref _totalTicks)).TotalSeconds;

    public double MaxDurationSeconds => TimeSpan.FromTicks(Interlocked.Read(ref _maxTicks)).TotalSeconds;

    public void Record(TimeSpan duration)
    {
        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _totalTicks, duration.Ticks);

        var current = Interlocked.Read(ref _maxTicks);
        while (duration.Ticks > current)
        {
            var previous = Interlocked.CompareExchange(ref _maxTicks, duration.Ticks, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}

