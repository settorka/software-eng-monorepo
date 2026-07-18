using Settlement.Infrastructure.Persistence.Entities;

namespace Settlement.Infrastructure.Outbox;

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken);
}
