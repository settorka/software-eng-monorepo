namespace Settlement.Infrastructure.Outbox;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string Username { get; init; } = "guest";

    public string Password { get; init; } = "guest";

    public string Exchange { get; init; } = "settlement.events";

    public string RoutingKey { get; init; } = "payment.request.created";

    public int TimeoutMilliseconds { get; init; } = 5000;
}
