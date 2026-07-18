namespace Settlement.Application.Outbox;

public interface IOutboxDispatcher
{
    Task<int> DispatchAsync(int batchSize, int maxAttempts, CancellationToken cancellationToken);
}
