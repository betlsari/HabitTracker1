using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class FlowersIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public FlowersIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetFlower_FirstCall_CreatesAndReturnsSeedStage()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/flowers");

        response.EnsureSuccessStatusCode();
        var flower = await response.Content.ReadFromJsonAsync<FlowerResponse>();
        Assert.Equal(0, flower!.Level);
        Assert.Equal("Seed", flower.Stage);
    }

    [Fact]
    public async Task WaterHabitCompletion_IncreasesFlowerLevel()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createHabit = await client.PostAsJsonAsync("/api/habits", new
        {
            name = "Su içme",
            category = "Su",
            dailyGoal = 5,
            period = "Daily"
        });
        createHabit.EnsureSuccessStatusCode();
        var habit = await createHabit.Content.ReadFromJsonAsync<HabitResponse>();

        var completion = await client.PostAsJsonAsync($"/api/habits/{habit!.Id}/habitcompletions", new
        {
            completionDate = DateTime.UtcNow,
            amount = 12
        });
        completion.EnsureSuccessStatusCode();

        var flowerResponse = await client.GetAsync("/api/flowers");
        var flower = await flowerResponse.Content.ReadFromJsonAsync<FlowerResponse>();

        Assert.True(flower!.Level >= 1);
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/flowers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class HabitResponse
    {
        public int Id { get; set; }
    }

    private sealed class FlowerResponse
    {
        public int Level { get; set; }
        public string Stage { get; set; } = string.Empty;
    }
}