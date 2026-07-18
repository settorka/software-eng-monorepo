namespace Settlement.Infrastructure.Outbox;

public sealed class OutboxPublisherOptions
{
    public const string SectionName = "OutboxPublisher";

    public string Mode { get; init; } = "Logging";

    public string Endpoint { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public int TimeoutMilliseconds { get; init; } = 5000;

    public int MaxPayloadBytes { get; init; } = 262144;
}
