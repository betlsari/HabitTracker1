using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public class NotificationService
{
    private readonly AppDbContext _context;
    private readonly IPushNotificationSender _pushSender;

    public NotificationService(AppDbContext context, IPushNotificationSender pushSender)
    {
        _context = context;
        _pushSender = pushSender;
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

        var tokens = await _context.DeviceTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        await _pushSender.SendAsync(tokens, title, body, cancellationToken);
        return true;
    }

    // DÜZELTİLDİ: Sayfalama eklendi. Önceden en fazla son 100 bildirim sabit
    // olarak dönüyordu ve daha eskilere erişmenin bir yolu yoktu. HabitsController/
    // BooksController'daki page/pageSize deseniyle tutarlı hale getirildi.
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

    // YENİ: Tek bir bildirimi siler. Kullanıcının kendi bildirimi olduğu
    // (userId eşleşmesi) kontrol edilir.
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

    // YENİ: Kullanıcının okunmuş (IsRead = true) tüm bildirimlerini temizler.
    // Önceden sadece "okundu işaretleme" vardı, bildirim geçmişini temizlemenin
    // hiçbir yolu yoktu.
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
}