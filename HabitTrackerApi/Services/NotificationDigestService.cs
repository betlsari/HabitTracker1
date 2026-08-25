// HabitTrackerApi/Services/NotificationDigestService.cs
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
        // DÜZELTİLDİ (🟠 N+1): Önceden ilk sorgu zaten Users ile join yapıp
        // TimeZoneId'yi ÇEKMİYORDU (sadece Email alıyordu), sonra döngü
        // içinde HER kullanıcı için AYRI bir _context.Users sorgusu atılarak
        // TimeZoneId tekrar okunuyordu. Artık TimeZoneId de ilk join'e dahil
        // edildi; döngü içindeki ikinci sorgu tamamen kaldırıldı. Bu servis
        // her dakika tetiklendiği için (NotificationDigestBackgroundService)
        // kullanıcı sayısı arttıkça bu N+1 giderek pahalılaşıyordu.
        var users = await _context.NotificationPreferences.AsNoTracking()
            .Where(p => p.DigestEnabled)
            .Join(_context.Users, p => p.UserId, u => u.Id, (p, u) => new
            {
                p.UserId,
                u.Email,
                u.TimeZoneId,
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

            // DÜZELTİLDİ: Ayrı sorgu yerine yukarıdaki join'den gelen
            // user.TimeZoneId kullanılıyor.
            var localNow = TimeZones.ToLocal(utcNow, TimeZones.Resolve(user.TimeZoneId));
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