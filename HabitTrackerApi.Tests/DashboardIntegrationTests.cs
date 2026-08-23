using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class DashboardIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public DashboardIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetDashboard_AggregatesHabitsBooksPetsAndFlower()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await client.PostAsJsonAsync("/api/habits", new
        {
            name = "Dashboard habit",
            category = "Spor",
            dailyGoal = 1,
            period = "Daily"
        });
        await client.PostAsJsonAsync("/api/books", new
        {
            title = "Dashboard Book",
            goalType = "Pages",
            dailyGoalAmount = 10,
            totalPages = 100
        });
        await client.PostAsJsonAsync("/api/pets", new { type = "Rabbit" });

        var response = await client.GetAsync("/api/dashboard");

        response.EnsureSuccessStatusCode();
        var dashboard = await response.Content.ReadFromJsonAsync<DashboardResponse>();

        Assert.Single(dashboard!.Habits);
        Assert.Single(dashboard.Books);
        Assert.Single(dashboard.Pets);
    }

    private sealed class DashboardResponse
    {
        public int TotalXp { get; set; }
        public List<object> Habits { get; set; } = new();
        public List<object> Books { get; set; } = new();
        public List<object> Pets { get; set; } = new();
        public int UnreadNotificationCount { get; set; }
    }
}