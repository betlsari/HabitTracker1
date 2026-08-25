using Models;

namespace Services;

public interface IPushOutboxProcessor
{
    Task<List<PushOutboxItem>> ClaimBatchAsync(int batchSize, string workerId, CancellationToken cancellationToken);

    Task MarkSentAsync(long id, CancellationToken cancellationToken);

    Task MarkFailedAsync(long id, string error, DateTime? nextAttemptAtUtc, CancellationToken cancellationToken);

    Task<int> PendingCountAsync(CancellationToken cancellationToken);
}
