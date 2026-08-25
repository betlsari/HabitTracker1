using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Data;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class NotificationsIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public NotificationsIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Authenticated_user_can_list_notifications()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/notifications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Marking_nonexistent_notification_read_returns_not_found()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/notifications/999999/read", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_user_can_fetch_one_of_their_notifications()
    {
        using var client = _factory.CreateClient();
        var tokenResult = await _factory.CreateAccessTokenWithEmailAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

        int notificationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Email == tokenResult.Email);
            var notification = new UserNotification
            {
                UserId = user.Id,
                Type = "Reminder",
                Title = "Test notification",
                Body = "Test body",
                DedupKey = $"test:{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            db.UserNotifications.Add(notification);
            await db.SaveChangesAsync();
            notificationId = notification.Id;
        }

        var response = await client.GetAsync($"/api/notifications/{notificationId}");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<NotificationDtoShape>();
        Assert.Equal(notificationId, result!.Id);
        Assert.Equal("Test notification", result.Title);
    }

    private sealed class NotificationDtoShape
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }
}