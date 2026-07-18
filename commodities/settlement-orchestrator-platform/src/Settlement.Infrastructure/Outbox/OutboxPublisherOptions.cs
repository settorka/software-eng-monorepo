namespace Settlement.Infrastructure.Outbox;

public sealed class OutboxPublisherOptions
{
    public const string SectionName = "OutboxPublisher";

    public string Mode { get; init; } = "Logging";

    public int MaxPayloadBytes { get; init; } = 262144;
}
