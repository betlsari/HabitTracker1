using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public sealed class NotificationDigestService
{
    private readonly AppDbContext _context;
    private readonly IEmailQueue _emailQueue;

    public NotificationDigestService(AppDbContext context, IEmailQueue emailQueue)
    {
        _context = context;
        _emailQueue = emailQueue;
    }

    public async Task ProcessAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var users = await _context.NotificationPreferences.AsNoTracking()
            .Where(p => p.DigestEnabled)
            .Join(_context.Users, p => p.UserId, u => u.Id, (p, u) => new
            {
                p.UserId,
                u.Email,
                p.DigestHourUtc,
                p.QuietHoursStart,
                p.QuietHoursEnd
            })
            .Where(x => x.Email != null)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var digestDate = DateOnly.FromDateTime(utcNow.AddDays(-1));
            var start = digestDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = start.AddDays(1);
            var alreadySent = await _context.NotificationDigestDeliveries
                .AnyAsync(d => d.UserId == user.UserId && d.DigestDate == digestDate, cancellationToken);
            if (alreadySent)
            {
                continue;
            }

            // Quiet hours defer the digest; notifications remain in the digest
            // and are sent on the next worker tick after quiet hours end.
            var timeZoneId = await _context.Users.AsNoTracking()
                .Where(u => u.Id == user.UserId)
                .Select(u => u.TimeZoneId)
                .FirstOrDefaultAsync(cancellationToken);
            var localNow = TimeZones.ToLocal(utcNow, TimeZones.Resolve(timeZoneId));
            if (IsWithinQuietHours(localNow, user.QuietHoursStart, user.QuietHoursEnd))
            {
                continue;
            }

            var notifications = await _context.UserNotifications.AsNoTracking()
                .Where(n => n.UserId == user.UserId && n.CreatedAt >= start && n.CreatedAt < end)
                .OrderBy(n => n.CreatedAt)
                .Select(n => new { n.Title, n.Body })
                .ToListAsync(cancellationToken);
            if (notifications.Count == 0)
            {
                continue;
            }

            var body = string.Join(Environment.NewLine, notifications.Select(n => $"- {n.Title}: {n.Body}"));
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                await _emailQueue.EnqueueAsync(new EmailMessage(
                    user.Email!,
                    $"HabitTracker günlük bildirim özeti - {digestDate:yyyy-MM-dd}",
                    body), cancellationToken);

                _context.NotificationDigestDeliveries.Add(new NotificationDigestDelivery
                {
                    UserId = user.UserId,
                    DigestDate = digestDate,
                    CreatedAt = utcNow
                });
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                continue;
            }
        }
    }


    private static bool IsWithinQuietHours(DateTime localNow, TimeOnly? start, TimeOnly? end)
    {
        if (!start.HasValue || !end.HasValue)
        {
            return false;
        }

        var localTime = TimeOnly.FromDateTime(localNow);
        return start <= end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
    }
}