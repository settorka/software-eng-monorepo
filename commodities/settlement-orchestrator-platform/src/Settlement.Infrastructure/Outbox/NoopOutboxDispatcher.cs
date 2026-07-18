using Settlement.Application.Outbox;

namespace Settlement.Infrastructure.Outbox;

public sealed class NoopOutboxDispatcher : IOutboxDispatcher
{
    public Task<int> DispatchAsync(int batchSize, int maxAttempts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0);
    }
}
