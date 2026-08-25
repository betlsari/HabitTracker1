using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public class NotificationService
{
    private readonly AppDbContext _context;
    private readonly IPushQueue _pushQueue;

   
    public NotificationService(AppDbContext context, IPushQueue pushQueue)
    {
        _context = context;
        _pushQueue = pushQueue;
    }

    public async Task<bool> TryEnqueueAsync(
        string userId,
        string type,
        string title,
        string body,
        int? habitId,
        string dedupKey,
        CancellationToken cancellationToken = default)
    {
        var exists = await _context.UserNotifications.AnyAsync(n => n.DedupKey == dedupKey, cancellationToken);
        if (exists)
        {
            return false;
        }

        var preference = await _context.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (preference != null && IsTypeDisabled(preference, type))
        {
            return false;
        }

        _context.UserNotifications.Add(new UserNotification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            HabitId = habitId,
            DedupKey = dedupKey,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return false;
        }

        // Sessiz saatler içindeyse bildirim kaydı DB'ye yazılır (kullanıcı
        // uygulamayı açtığında görsün diye) ama push kuyruğuna hiç
        // eklenmez.
        if (preference != null && await IsWithinQuietHoursAsync(userId, preference, cancellationToken))
        {
            return true;
        }

        // DÜZELTİLDİ: Doğrudan gönderim yerine kalıcı kuyruğa yazılıyor.
        // Cihaz token'ları burada OKUNMUYOR — worker gönderim anında güncel
        // listeyi kendisi çeker (bkz. PushSenderBackgroundService).
        await _pushQueue.EnqueueAsync(userId, title, body, cancellationToken);
        return true;
    }

    public async Task<PagedResultDto<NotificationDto>> ListAsync(
        string userId,
        bool unreadOnly,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var query = _context.UserNotifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        query = query.OrderByDescending(n => n.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                Body = n.Body,
                HabitId = n.HabitId,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResultDto<NotificationDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<bool> MarkReadAsync(string userId, int id, CancellationToken cancellationToken = default)
    {
        var notification = await _context.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        notification.IsRead = true;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<NotificationDto?> GetAsync(string userId, int id, CancellationToken cancellationToken = default)
    {
        return await _context.UserNotifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Id == id)
            .Select(n => new NotificationDto
            {
                Id = n.Id, Type = n.Type, Title = n.Title, Body = n.Body,
                HabitId = n.HabitId, IsRead = n.IsRead, CreatedAt = n.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> MarkUnreadAsync(string userId, int id, CancellationToken cancellationToken = default)
    {
        var notification = await _context.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        notification.IsRead = false;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> MarkAllReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var unread = await _context.UserNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var notification in unread)
        {
            notification.IsRead = true;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return unread.Count;
    }

    public async Task<bool> DeleteAsync(string userId, int id, CancellationToken cancellationToken = default)
    {
        var notification = await _context.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        _context.UserNotifications.Remove(notification);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> DeleteAllReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var read = await _context.UserNotifications
            .Where(n => n.UserId == userId && n.IsRead)
            .ToListAsync(cancellationToken);

        if (read.Count == 0)
        {
            return 0;
        }

        _context.UserNotifications.RemoveRange(read);
        await _context.SaveChangesAsync(cancellationToken);
        return read.Count;
    }

    private static bool IsTypeDisabled(NotificationPreference preference, string type)
    {
        if (string.IsNullOrWhiteSpace(preference.DisabledTypes))
        {
            return false;
        }

        return preference.DisabledTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Contains(type, StringComparer.Ordinal);
    }

    private async Task<bool> IsWithinQuietHoursAsync(
        string userId, NotificationPreference preference, CancellationToken cancellationToken)
    {
        if (preference.QuietHoursStart is not { } start || preference.QuietHoursEnd is not { } end)
        {
            return false;
        }

        var user = await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TimeZoneId })
            .FirstOrDefaultAsync(cancellationToken);

        var tz = TimeZones.Resolve(user?.TimeZoneId);
        var localNow = TimeZones.ToLocal(DateTime.UtcNow, tz);
        var localTime = TimeOnly.FromDateTime(localNow);

        return start <= end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
    }
}