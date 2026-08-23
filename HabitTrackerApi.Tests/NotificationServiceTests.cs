using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class NotificationServiceTests
{
    private sealed class RecordingPushSender : IPushNotificationSender
    {
        public int CallCount { get; private set; }

        public Task SendAsync(IReadOnlyList<string> deviceTokens, string title, string body, CancellationToken cancellationToken = default)
        {
            CallCount++;
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
        var service = new NotificationService(context, new RecordingPushSender());

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
        var service = new NotificationService(context, new RecordingPushSender());

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
    public async Task MarkAllReadAsync_MarksOnlyUnreadForGivenUser()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new NotificationService(context, new RecordingPushSender());

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
        var service = new NotificationService(context, new RecordingPushSender());

        await service.TryEnqueueAsync("user-1", NotificationTypes.Reminder, "A", "A", null, "k1");
        await service.TryEnqueueAsync("user-1", NotificationTypes.Missed, "B", "B", null, "k2");
        await service.MarkAllReadAsync("user-1");
        await service.TryEnqueueAsync("user-1", NotificationTypes.GoalReached, "C", "C", null, "k3");

        var deleted = await service.DeleteAllReadAsync("user-1");

        Assert.Equal(2, deleted);
        Assert.Equal(1, await context.UserNotifications.CountAsync(n => n.UserId == "user-1"));
    }
}