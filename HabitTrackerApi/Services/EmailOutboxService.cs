
using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;


public sealed class EmailOutboxService : IEmailQueue, IEmailOutboxProcessor
{
    private readonly AppDbContext _context;

    public EmailOutboxService(AppDbContext context)
    {
        _context = context;
    }

    public async Task EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _context.EmailOutboxItems.Add(new EmailOutboxItem
        {
            ToEmail = message.ToEmail,
            Subject = message.Subject,
            Body = message.Body,
            Status = EmailOutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<EmailOutboxItem>> ClaimBatchAsync(
        int batchSize, string workerId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        
        var claimed = await _context.EmailOutboxItems.FromSqlInterpolated($@"
            UPDATE ""EmailOutboxItems""
            SET ""Status"" = {(int)EmailOutboxStatus.Processing},
                ""LockedAt"" = {now},
                ""LockedBy"" = {workerId}
            WHERE ""Id"" = ANY (
                SELECT ""Id"" FROM ""EmailOutboxItems""
                WHERE ""Status"" = {(int)EmailOutboxStatus.Pending}
                  AND (""NextAttemptAt"" IS NULL OR ""NextAttemptAt"" <= {now})
                ORDER BY ""CreatedAt""
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
            )
            RETURNING *;
        ").AsNoTracking().ToListAsync(cancellationToken);

        return claimed;
    }

    public async Task MarkSentAsync(long id, CancellationToken cancellationToken)
    {
        var item = await _context.EmailOutboxItems.FindAsync(new object[] { id }, cancellationToken);
        if (item == null)
        {
            return;
        }

        item.Status = EmailOutboxStatus.Sent;
        item.SentAt = DateTime.UtcNow;
        item.LockedAt = null;
        item.LockedBy = null;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        long id, string error, DateTime? nextAttemptAtUtc, CancellationToken cancellationToken)
    {
        var item = await _context.EmailOutboxItems.FindAsync(new object[] { id }, cancellationToken);
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
            item.Status = EmailOutboxStatus.Pending;
            item.NextAttemptAt = nextAttemptAtUtc;
        }
        else
        {
            item.Status = EmailOutboxStatus.Failed;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task MoveToDeadLetterAsync(long id, string error, CancellationToken cancellationToken)
    {
        var item = await _context.EmailOutboxItems.FindAsync(new object[] { id }, cancellationToken);
        if (item == null)
        {
            return;
        }

        _context.EmailDeadLetters.Add(new EmailDeadLetter
        {
            OriginalOutboxId = item.Id,
            ToEmail = item.ToEmail,
            Subject = item.Subject,
            Body = item.Body,
            AttemptCount = item.AttemptCount + 1,
            LastError = error.Length > 2000 ? error[..2000] : error,
            FailedAt = DateTime.UtcNow
        });
        _context.EmailOutboxItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<int> PendingCountAsync(CancellationToken cancellationToken) =>
        _context.EmailOutboxItems.CountAsync(
            e => e.Status == EmailOutboxStatus.Pending || e.Status == EmailOutboxStatus.Processing,
            cancellationToken);
}