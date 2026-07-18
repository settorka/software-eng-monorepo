using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Settlement.Infrastructure.Persistence.Entities;

namespace Settlement.Infrastructure.Outbox;

public sealed class RabbitMqOutboxPublisher(
    IOptions<RabbitMqOptions> options,
    IOptions<OutboxPublisherOptions> publisherOptions,
    ILogger<RabbitMqOutboxPublisher> logger) : IOutboxPublisher
{
    private readonly RabbitMqOptions options = options.Value;
    private readonly OutboxPublisherOptions publisherOptions = publisherOptions.Value;

    public async Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
    {
        Validate(message);

        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.Username,
            Password = options.Password,
            RequestedConnectionTimeout = TimeSpan.FromMilliseconds(Math.Max(100, options.TimeoutMilliseconds))
        };

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: options.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        var body = Encoding.UTF8.GetBytes(message.Payload);
        var properties = new BasicProperties
        {
            AppId = "settlement-orchestrator",
            ContentType = "application/json",
            CorrelationId = message.WorkflowId.ToString("N"),
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.OutboxMessageId.ToString("N"),
            Timestamp = new AmqpTimestamp(message.CreatedAt.ToUnixTimeSeconds()),
            Type = message.MessageType,
            Headers = new Dictionary<string, object?>
            {
                ["payload-hash"] = message.PayloadHash,
                ["workflow-id"] = message.WorkflowId.ToString("N")
            }
        };

        await channel.BasicPublishAsync(
            exchange: options.Exchange,
            routingKey: options.RoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Published outbox message {OutboxMessageId} to RabbitMQ exchange {Exchange} with routing key {RoutingKey}.",
            message.OutboxMessageId,
            options.Exchange,
            options.RoutingKey);
    }

    private void Validate(OutboxMessageEntity message)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new InvalidOperationException("RabbitMq:Host is required.");
        }

        if (options.Port < 1)
        {
            throw new InvalidOperationException("RabbitMq:Port must be positive.");
        }

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            throw new InvalidOperationException("RabbitMq:Username is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Exchange))
        {
            throw new InvalidOperationException("RabbitMq:Exchange is required.");
        }

        if (string.IsNullOrWhiteSpace(options.RoutingKey))
        {
            throw new InvalidOperationException("RabbitMq:RoutingKey is required.");
        }

        var payloadBytes = Encoding.UTF8.GetByteCount(message.Payload);
        if (payloadBytes > publisherOptions.MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Outbox payload is {payloadBytes} bytes, above configured limit {publisherOptions.MaxPayloadBytes} bytes.");
        }
    }
}
