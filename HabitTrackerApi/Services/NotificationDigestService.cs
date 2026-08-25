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
        var currentHour = utcNow.Hour;
        var digestDate = DateOnly.FromDateTime(utcNow.AddDays(-1));
        var start = digestDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var users = await _context.NotificationPreferences.AsNoTracking()
            .Where(p => p.DigestEnabled && p.DigestHourUtc == currentHour)
            .Join(_context.Users, p => p.UserId, u => u.Id, (p, u) => new { p.UserId, u.Email })
            .Where(x => x.Email != null)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var alreadySent = await _context.NotificationDigestDeliveries
                .AnyAsync(d => d.UserId == user.UserId && d.DigestDate == digestDate, cancellationToken);
            if (alreadySent)
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
}