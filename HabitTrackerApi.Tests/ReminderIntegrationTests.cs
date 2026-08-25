using System.Net.Http.Headers;
using System.Net.Http.Json;
using Data;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class ReminderIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ReminderIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ReminderService_creates_a_reminder_notification_at_configured_time()
    {
        using var client = _factory.CreateClient();
        var tokenResult = await _factory.CreateAccessTokenWithEmailAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);
        var create = await client.PostAsJsonAsync("/api/habits", new
        {
            name = $"Reminder {Guid.NewGuid():N}",
            category = "Diğer",
            customCategoryName = "Testing",
            dailyGoal = 10,
            period = "Daily",
            reminderTime = "10:15:00"
        });
        create.EnsureSuccessStatusCode();
        var habit = await create.Content.ReadFromJsonAsync<IdResponse>();

        var tz = TimeZones.Resolve("Europe/Istanbul");
        var localNow = DateTime.Today.AddHours(10).AddMinutes(15);
        var utcNow = TimeZones.ToUtc(localNow, tz);
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ReminderService>();
            await service.ProcessAsync(utcNow);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Email == tokenResult.Email);
        Assert.Contains(db.UserNotifications, n => n.UserId == user.Id && n.HabitId == habit!.Id && n.Type == NotificationTypes.Reminder);
    }

    [Fact]
    public async Task ReminderService_creates_missed_notification_at_period_end()
    {
        using var client = _factory.CreateClient();
        var tokenResult = await _factory.CreateAccessTokenWithEmailAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);
        var create = await client.PostAsJsonAsync("/api/habits", new
        {
            name = $"Missed {Guid.NewGuid():N}",
            category = "Diğer",
            customCategoryName = "Testing",
            dailyGoal = 10,
            period = "Daily"
        });
        create.EnsureSuccessStatusCode();
        var habit = await create.Content.ReadFromJsonAsync<IdResponse>();

        var tz = TimeZones.Resolve("Europe/Istanbul");
        var localNow = DateTime.Today.AddHours(21);
        var utcNow = TimeZones.ToUtc(localNow, tz);
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ReminderService>();
            await service.ProcessAsync(utcNow);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Email == tokenResult.Email);
        Assert.Contains(db.UserNotifications, n => n.UserId == user.Id && n.HabitId == habit!.Id && n.Type == NotificationTypes.Missed);
    }

    private sealed class IdResponse
    {
        public int Id { get; set; }
    }
}
