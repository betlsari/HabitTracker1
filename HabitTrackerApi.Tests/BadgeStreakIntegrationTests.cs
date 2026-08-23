using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class BadgeStreakIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public BadgeStreakIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Three_consecutive_daily_goal_completions_award_Streak3_badge()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createHabit = await client.PostAsJsonAsync("/api/habits", new
        {
            name = "Streak habit",
            category = "Spor",
            dailyGoal = 1,
            period = "Daily"
        });
        createHabit.EnsureSuccessStatusCode();
        var habit = await createHabit.Content.ReadFromJsonAsync<HabitResponse>();

        
        var baseDate = DateTime.UtcNow.Date.AddDays(-2);
        for (var i = 0; i < 3; i++)
        {
            var completion = await client.PostAsJsonAsync($"/api/habits/{habit!.Id}/habitcompletions", new
            {
                completionDate = baseDate.AddDays(i).AddHours(10),
                amount = 1
            });
            completion.EnsureSuccessStatusCode();
        }

        var badgesResponse = await client.GetAsync("/api/badges");
        badgesResponse.EnsureSuccessStatusCode();
        var badges = await badgesResponse.Content.ReadFromJsonAsync<List<BadgeResponse>>();

        var streak3 = badges!.Single(b => b.Code == "STREAK_3");
        Assert.True(streak3.Earned);

        var streak7 = badges!.Single(b => b.Code == "STREAK_7");
        Assert.False(streak7.Earned);
    }

    private sealed class HabitResponse
    {
        public int Id { get; set; }
    }

    private sealed class BadgeResponse
    {
        public required string Code { get; set; }
        public bool Earned { get; set; }
    }
}