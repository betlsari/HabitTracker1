using Models;

namespace Services;

public interface IRecalculationOutboxProcessor
{
    Task<List<RecalculationOutboxItem>> ClaimBatchAsync(int batchSize, string workerId, CancellationToken cancellationToken);

    Task MarkCompletedAsync(long id, CancellationToken cancellationToken);

    Task MarkFailedAsync(long id, string error, DateTime? nextAttemptAtUtc, CancellationToken cancellationToken);

    Task<int> PendingCountAsync(CancellationToken cancellationToken);
}
