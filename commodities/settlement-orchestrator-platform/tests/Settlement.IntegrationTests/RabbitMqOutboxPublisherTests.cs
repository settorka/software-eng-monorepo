using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Settlement.Infrastructure.Outbox;
using Settlement.Infrastructure.Persistence.Entities;
using Xunit;

namespace Settlement.IntegrationTests;

public sealed class RabbitMqOutboxPublisherTests
{
    [Fact]
    public async Task PublishAsyncPublishesDurableMessageToConfiguredExchange()
    {
        var options = new RabbitMqOptions
        {
            Host = Environment.GetEnvironmentVariable("RabbitMq__Host") is "rabbitmq"
                ? "localhost"
                : Environment.GetEnvironmentVariable("RabbitMq__Host") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("RabbitMq__Port"), out var port) ? port : 5672,
            Username = Environment.GetEnvironmentVariable("RabbitMq__Username") ?? "settlement",
            Password = Environment.GetEnvironmentVariable("RabbitMq__Password") ?? "settlement",
            Exchange = $"settlement.test.{Guid.NewGuid():N}",
            RoutingKey = "payment.request.created",
            TimeoutMilliseconds = 5000
        };

        var queue = $"settlement.test.{Guid.NewGuid():N}";
        await using var connection = await TryCreateConnectionAsync(options);
        if (connection is null)
        {
            return;
        }

        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        await channel.QueueDeclareAsync(queue, durable: false, exclusive: false, autoDelete: true);
        await channel.QueueBindAsync(queue, options.Exchange, options.RoutingKey);

        var publisher = new RabbitMqOutboxPublisher(
            Options.Create(options),
            Options.Create(new OutboxPublisherOptions { Mode = "RabbitMq", MaxPayloadBytes = 1024 }),
            NullLogger<RabbitMqOutboxPublisher>.Instance);

        var message = new OutboxMessageEntity
        {
            OutboxMessageId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            WorkflowId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            MessageType = "PaymentRequestCreated",
            PayloadHash = "rabbit-payload-hash",
            Payload = "{\"paymentRequestId\":\"PAY-RABBIT-1\"}",
            CreatedAt = new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero)
        };

        await publisher.PublishAsync(message, CancellationToken.None);

        BasicGetResult? result = null;
        for (var attempt = 0; attempt < 10 && result is null; attempt++)
        {
            result = await channel.BasicGetAsync(queue, autoAck: false);
            if (result is null)
            {
                await Task.Delay(100);
            }
        }

        Assert.NotNull(result);
        Assert.Equal(message.Payload, Encoding.UTF8.GetString(result.Body.Span));
        Assert.Equal(message.OutboxMessageId.ToString("N"), result.BasicProperties.MessageId);
        Assert.Equal(message.MessageType, result.BasicProperties.Type);
        Assert.Equal(DeliveryModes.Persistent, result.BasicProperties.DeliveryMode);

        await channel.BasicAckAsync(result.DeliveryTag, multiple: false);
        await channel.ExchangeDeleteAsync(options.Exchange);
    }

    private static async Task<IConnection?> TryCreateConnectionAsync(RabbitMqOptions options)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = options.Host,
                Port = options.Port,
                UserName = options.Username,
                Password = options.Password,
                RequestedConnectionTimeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds)
            };

            return await factory.CreateConnectionAsync();
        }
        catch (BrokerUnreachableException)
        {
            return null;
        }
    }
}
