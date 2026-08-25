using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public sealed class RecalculationOutboxService : IRecalculationQueue, IRecalculationOutboxProcessor
{
    private readonly AppDbContext _context;

    public RecalculationOutboxService(AppDbContext context)
    {
        _context = context;
    }

    public async Task EnqueueHabitRecalculationAsync(int habitId, string userId, string? timeZoneId, CancellationToken cancellationToken = default)
    {
        _context.RecalculationOutboxItems.Add(new RecalculationOutboxItem
        {
            JobType = RecalculationJobType.Habit,
            HabitId = habitId,
            UserId = userId,
            TimeZoneId = timeZoneId,
            Status = RecalculationOutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task EnqueueBookRecalculationAsync(int bookId, string userId, string? timeZoneId, CancellationToken cancellationToken = default)
    {
        _context.RecalculationOutboxItems.Add(new RecalculationOutboxItem
        {
            JobType = RecalculationJobType.Book,
            BookId = bookId,
            UserId = userId,
            TimeZoneId = timeZoneId,
            Status = RecalculationOutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public int PendingCount => _context.RecalculationOutboxItems
        .Count(x => x.Status == RecalculationOutboxStatus.Pending || x.Status == RecalculationOutboxStatus.Processing);

    public async Task<List<RecalculationOutboxItem>> ClaimBatchAsync(int batchSize, string workerId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        if (_context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var sqliteBatch = await _context.RecalculationOutboxItems
                .Where(x => x.Status == RecalculationOutboxStatus.Pending &&
                            (x.NextAttemptAt == null || x.NextAttemptAt <= now))
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            foreach (var item in sqliteBatch)
            {
                item.Status = RecalculationOutboxStatus.Processing;
                item.LockedAt = now;
                item.LockedBy = workerId;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return sqliteBatch;
        }

        var claimed = await _context.RecalculationOutboxItems
            .FromSqlInterpolated($@"
                UPDATE ""RecalculationOutboxItems""
                SET ""Status"" = {(int)RecalculationOutboxStatus.Processing},
                    ""LockedAt"" = {now},
                    ""LockedBy"" = {workerId}
                WHERE ""Id"" = ANY (
                    SELECT ""Id"" FROM ""RecalculationOutboxItems""
                    WHERE ""Status"" = {(int)RecalculationOutboxStatus.Pending}
                      AND (""NextAttemptAt"" IS NULL OR ""NextAttemptAt"" <= {now})
                    ORDER BY ""CreatedAt""
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING *;
            ")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return claimed;
    }

    public async Task MarkCompletedAsync(long id, CancellationToken cancellationToken)
    {
        var item = await _context.RecalculationOutboxItems.FindAsync(new object[] { id }, cancellationToken);
        if (item == null)
        {
            return;
        }

        item.Status = RecalculationOutboxStatus.Completed;
        item.CompletedAt = DateTime.UtcNow;
        item.LockedAt = null;
        item.LockedBy = null;
        item.NextAttemptAt = null;
        item.LastError = null;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(long id, string error, DateTime? nextAttemptAtUtc, CancellationToken cancellationToken)
    {
        var item = await _context.RecalculationOutboxItems.FindAsync(new object[] { id }, cancellationToken);
        if (item == null)
        {
            return;
        }

        item.AttemptCount++;
        item.LastError = error.Length > 2000 ? error[..2000] : error;
        item.LockedAt = null;
        item.LockedBy = null;

        if (nextAttemptAtUtc.HasValue)
        {
            item.Status = RecalculationOutboxStatus.Pending;
            item.NextAttemptAt = nextAttemptAtUtc;
        }
        else
        {
            item.Status = RecalculationOutboxStatus.Failed;
            item.NextAttemptAt = null;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<int> PendingCountAsync(CancellationToken cancellationToken) =>
        _context.RecalculationOutboxItems.CountAsync(
            e => e.Status == RecalculationOutboxStatus.Pending || e.Status == RecalculationOutboxStatus.Processing,
            cancellationToken);
}
