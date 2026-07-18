using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Settlement.Application.Common;
using Settlement.Application.Outbox;
using Settlement.Infrastructure.Persistence;
using Settlement.Infrastructure.Persistence.Entities;

namespace Settlement.Infrastructure.Outbox;

public sealed class OracleOutboxDispatcher(
    SettlementDbContext dbContext,
    IOutboxPublisher publisher,
    IClock clock,
    ILogger<OracleOutboxDispatcher> logger) : IOutboxDispatcher
{
    public const string ActivitySourceName = "Settlement.Outbox";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public async Task<int> DispatchAsync(int batchSize, int maxAttempts, CancellationToken cancellationToken)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Outbox batch size must be positive.");
        }

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Outbox max attempts must be positive.");
        }

        var now = clock.UtcNow;
        var messages = await dbContext.OutboxMessages
            .Where(message =>
                message.Status == "Pending" &&
                message.PublishedAt == null &&
                message.DeadLetteredAt == null &&
                (message.NextAttemptAt == null || message.NextAttemptAt <= now))
            .OrderBy(message => message.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var published = 0;

        foreach (var message in messages)
        {
            using var activity = ActivitySource.StartActivity("outbox.publish", ActivityKind.Producer);
            activity?.SetTag("outbox.message_id", message.OutboxMessageId);
            activity?.SetTag("outbox.message_type", message.MessageType);
            activity?.SetTag("workflow.id", message.WorkflowId);

            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                MarkPublished(message, clock.UtcNow);
                activity?.SetTag("outbox.status", "Published");
                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                MarkFailed(message, exception, maxAttempts, clock.UtcNow);
                activity?.SetTag("outbox.status", message.Status);
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return published;
    }

    private static void MarkPublished(OutboxMessageEntity message, DateTimeOffset now)
    {
        message.Status = "Published";
        message.PublishedAt = now;
        message.LastError = null;
        message.NextAttemptAt = null;
    }

    private void MarkFailed(
        OutboxMessageEntity message,
        Exception exception,
        int maxAttempts,
        DateTimeOffset now)
    {
        message.AttemptCount++;
        message.LastError = exception.Message;

        if (message.AttemptCount >= maxAttempts)
        {
            message.Status = "DeadLetter";
            message.DeadLetteredAt = now;
            message.NextAttemptAt = null;

            logger.LogError(
                exception,
                "Outbox message {OutboxMessageId} moved to dead-letter after {AttemptCount} attempts.",
                message.OutboxMessageId,
                message.AttemptCount);
            return;
        }

        var delaySeconds = Math.Min(Math.Pow(2, message.AttemptCount), 300);
        message.NextAttemptAt = now.AddSeconds(delaySeconds);

        logger.LogWarning(
            exception,
            "Outbox message {OutboxMessageId} publish attempt {AttemptCount} failed; next attempt at {NextAttemptAt}.",
            message.OutboxMessageId,
            message.AttemptCount,
            message.NextAttemptAt);
    }
}
