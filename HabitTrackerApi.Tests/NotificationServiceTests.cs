using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class NotificationServiceTests
{
    // DÜZELTİLDİ: NotificationService artık IPushQueue değil
    // IPushNotificationSender alıyor (push bildirimleri artık kalıcı
    // outbox'a yazılmıyor, doğrudan senkron gönderiliyor — bkz.
    // Services/NotificationService.cs).
    private sealed class RecordingPushSender : IPushNotificationSender
    {
        public int SendCount { get; private set; }
        public List<(string Title, string Body)> Sent { get; } = new();

        public Task SendAsync(IReadOnlyList<string> deviceTokens, string title, string body, CancellationToken cancellationToken = default)
        {
            SendCount++;
            Sent.Add((title, body));
            return Task.CompletedTask;
        }
    }

    private static AppDbContext CreateContext(string dbName) =>
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    // Push gönderimi artık cihaz token'ı olan kullanıcılar için tetiklendiği
    // için, push'un gerçekten çağrıldığını doğrulayan testlerde önce bir
    // DeviceToken eklememiz gerekiyor; aksi halde SendAsync hiç çağrılmaz.
    private static async Task AddDeviceTokenAsync(AppDbContext context, string userId)
    {
        context.DeviceTokens.Add(new DeviceToken
        {
            UserId = userId,
            Token = $"tok-{Guid.NewGuid():N}",
            Platform = "ios",
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task TryEnqueueAsync_DuplicateDedupKey_IsIgnored()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new NotificationService(context, new RecordingPushSender(), NullLogger<NotificationService>.Instance);

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
        var service = new NotificationService(context, new RecordingPushSender(), NullLogger<NotificationService>.Instance);

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
    public async Task TryEnqueueAsync_ValidNotification_SendsPushExactlyOnce()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        await AddDeviceTokenAsync(context, "user-1");

        var pushSender = new RecordingPushSender();
        var service = new NotificationService(context, pushSender, NullLogger<NotificationService>.Instance);

        var result = await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "Başlık", "Gövde", null, "dedup-push-1");

        Assert.True(result);
        Assert.Equal(1, pushSender.SendCount);
        Assert.Equal("Başlık", pushSender.Sent[0].Title);
    }

    [Fact]
    public async Task TryEnqueueAsync_NoDeviceTokens_DoesNotSendPush()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var pushSender = new RecordingPushSender();
        var service = new NotificationService(context, pushSender, NullLogger<NotificationService>.Instance);

        var result = await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "Başlık", "Gövde", null, "dedup-nodevice-1");

        Assert.True(result);
        Assert.Equal(0, pushSender.SendCount);
    }

   

    [Fact]
    public async Task MarkAllReadAsync_MarksOnlyUnreadForGivenUser()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new NotificationService(context, new RecordingPushSender(), NullLogger<NotificationService>.Instance);

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
        var service = new NotificationService(context, new RecordingPushSender(), NullLogger<NotificationService>.Instance);

        await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "A", "A", null, "k1");
        await service.TryEnqueueAsync("user-1", NotificationTypes.Missed, "B", "B", null, "k2");
        await service.MarkAllReadAsync("user-1");
        await service.TryEnqueueAsync("user-1", NotificationTypes.GoalReached, "C", "C", null, "k3");

        var deleted = await service.DeleteAllReadAsync("user-1");

        Assert.Equal(2, deleted);
        Assert.Equal(1, await context.UserNotifications.CountAsync(n => n.UserId == "user-1"));
    }
}