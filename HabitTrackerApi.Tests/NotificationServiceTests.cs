using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class NotificationServiceTests
{
    // DÜZELTİLDİ: NotificationService artık IPushNotificationSender değil
    // IPushQueue alıyor (push bildirimleri artık senkron gönderilmiyor,
    // kalıcı outbox'a yazılıyor — bkz. Services/PushOutboxService.cs).
    private sealed class RecordingPushQueue : IPushQueue
    {
        public int EnqueueCount { get; private set; }
        public List<(string UserId, string Title, string Body)> Enqueued { get; } = new();

        public Task EnqueueAsync(string userId, string title, string body, CancellationToken cancellationToken = default)
        {
            EnqueueCount++;
            Enqueued.Add((userId, title, body));
            return Task.CompletedTask;
        }
    }

    private static AppDbContext CreateContext(string dbName) =>
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    [Fact]
    public async Task TryEnqueueAsync_DuplicateDedupKey_IsIgnored()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new NotificationService(context, new RecordingPushQueue());

        var first = await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "Başlık", "Gövde", null, "dedup-1");
        var second = await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "Başlık", "Gövde", null, "dedup-1");

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, await context.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task TryEnqueueAsync_DisabledType_DoesNotCreateNotification()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new NotificationService(context, new RecordingPushQueue());

        context.NotificationPreferences.Add(new NotificationPreference
        {
            UserId = "user-1",
            DisabledTypes = NotificationTypes.Reminder
        });
        await context.SaveChangesAsync();

        var result = await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "Başlık", "Gövde", null, "dedup-2");

        Assert.False(result);
        Assert.Equal(0, await context.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task TryEnqueueAsync_ValidNotification_EnqueuesPushExactlyOnce()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var pushQueue = new RecordingPushQueue();
        var service = new NotificationService(context, pushQueue);

        var result = await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "Başlık", "Gövde", null, "dedup-push-1");

        Assert.True(result);
        Assert.Equal(1, pushQueue.EnqueueCount);
        Assert.Equal("user-1", pushQueue.Enqueued[0].UserId);
    }

    [Fact]
    public async Task TryEnqueueAsync_WithinQuietHours_CreatesNotificationButDoesNotEnqueuePush()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var pushQueue = new RecordingPushQueue();
        var service = new NotificationService(context, pushQueue);

        // Gün boyu süren bir sessiz saat aralığı tanımla (00:00 - 23:59)
        // böylece test, gerçek saat ne olursa olsun içinde kalsın.
        context.NotificationPreferences.Add(new NotificationPreference
        {
            UserId = "user-1",
            QuietHoursStart = new TimeOnly(0, 0),
            QuietHoursEnd = new TimeOnly(23, 59)
        });
        await context.SaveChangesAsync();

        var result = await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "Başlık", "Gövde", null, "dedup-quiet-1");

        Assert.True(result);
        Assert.Equal(1, await context.UserNotifications.CountAsync());
        Assert.Equal(0, pushQueue.EnqueueCount);
    }

    [Fact]
    public async Task MarkAllReadAsync_MarksOnlyUnreadForGivenUser()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new NotificationService(context, new RecordingPushQueue());

        await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "A", "A", null, "k1");
        await service.TryEnqueueAsync("user-1", NotificationTypes.Missed, "B", "B", null, "k2");
        await service.TryEnqueueAsync("user-2", NotificationTypes.Missed, "C", "C", null, "k3");

        var count = await service.MarkAllReadAsync("user-1");

        Assert.Equal(2, count);
        Assert.True(await context.UserNotifications.Where(n => n.UserId == "user-1").AllAsync(n => n.IsRead));

        var user2Notification = await context.UserNotifications.SingleAsync(n => n.UserId == "user-2");
        Assert.False(user2Notification.IsRead);
    }

    [Fact]
    public async Task DeleteAllReadAsync_RemovesOnlyReadNotifications()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new NotificationService(context, new RecordingPushQueue());

        await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "A", "A", null, "k1");
        await service.TryEnqueueAsync("user-1", NotificationTypes.Missed, "B", "B", null, "k2");
        await service.MarkAllReadAsync("user-1");
        await service.TryEnqueueAsync("user-1", NotificationTypes.GoalReached, "C", "C", null, "k3");

        var deleted = await service.DeleteAllReadAsync("user-1");

        Assert.Equal(2, deleted);
        Assert.Equal(1, await context.UserNotifications.CountAsync(n => n.UserId == "user-1"));
    }
}