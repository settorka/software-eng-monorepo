using Microsoft.Extensions.Logging;
using Settlement.Infrastructure.Persistence.Entities;

namespace Settlement.Infrastructure.Outbox;

public sealed class LoggingOutboxPublisher(ILogger<LoggingOutboxPublisher> logger) : IOutboxPublisher
{
    public Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation(
            "Published outbox message {OutboxMessageId} of type {MessageType} for workflow {WorkflowId}.",
            message.OutboxMessageId,
            message.MessageType,
            message.WorkflowId);

        return Task.CompletedTask;
    }
}
