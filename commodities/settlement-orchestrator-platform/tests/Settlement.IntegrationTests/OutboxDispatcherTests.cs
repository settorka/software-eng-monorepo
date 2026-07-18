using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Settlement.Application.Common;
using Settlement.Infrastructure.Outbox;
using Settlement.Infrastructure.Persistence;
using Settlement.Infrastructure.Persistence.Entities;
using Xunit;

namespace Settlement.IntegrationTests;

public sealed class OutboxDispatcherTests
{
    [Fact]
    public async Task PendingOutboxMessageIsMarkedPublishedAfterPublisherAcceptsIt()
    {
        await using var dbContext = CreateDbContext();
        var message = CreateMessage();
        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync();

        var dispatcher = new OracleOutboxDispatcher(
            dbContext,
            new RecordingPublisher(),
            new FixedClock(),
            NullLogger<OracleOutboxDispatcher>.Instance);

        var published = await dispatcher.DispatchAsync(10, 3, CancellationToken.None);

        Assert.Equal(1, published);
        Assert.Equal("Published", message.Status);
        Assert.NotNull(message.PublishedAt);
        Assert.Null(message.NextAttemptAt);
    }

    [Fact]
    public async Task ExhaustedOutboxMessageMovesToDeadLetter()
    {
        await using var dbContext = CreateDbContext();
        var message = CreateMessage();
        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync();

        var dispatcher = new OracleOutboxDispatcher(
            dbContext,
            new FailingPublisher(),
            new FixedClock(),
            NullLogger<OracleOutboxDispatcher>.Instance);

        var published = await dispatcher.DispatchAsync(10, 1, CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Equal("DeadLetter", message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.DeadLetteredAt);
        Assert.Null(message.NextAttemptAt);
    }

    private static SettlementDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new SettlementDbContext(options);
    }

    private static OutboxMessageEntity CreateMessage()
    {
        return new OutboxMessageEntity
        {
            OutboxMessageId = Guid.NewGuid(),
            WorkflowId = Guid.NewGuid(),
            MessageType = "PaymentRequestCreated",
            Payload = "payload",
            PayloadHash = "hash",
            Status = "Pending",
            CreatedAt = FixedClock.Now,
            NextAttemptAt = FixedClock.Now
        };
    }

    private sealed class RecordingPublisher : IOutboxPublisher
    {
        public Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FailingPublisher : IOutboxPublisher
    {
        public Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Publisher unavailable.");
        }
    }

    private sealed class FixedClock : IClock
    {
        public static readonly DateTimeOffset Now = new(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => Now;
    }
}
