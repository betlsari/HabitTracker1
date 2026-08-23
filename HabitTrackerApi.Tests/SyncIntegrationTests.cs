using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class SyncIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public SyncIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Batch_sync_with_no_items_returns_bad_request()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/sync/batch", new
        {
            habitCompletions = new object[0],
            bookReadingLogs = new object[0]
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Batch_sync_creates_habit_completion_and_returns_success_result()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createHabit = await client.PostAsJsonAsync("/api/habits", new
        {
            name = "Sync habit",
            category = "Su",
            dailyGoal = 5,
            period = "Daily"
        });
        createHabit.EnsureSuccessStatusCode();
        var habit = await createHabit.Content.ReadFromJsonAsync<HabitResponse>();

        var clientRequestId = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/sync/batch", new
        {
            habitCompletions = new[]
            {
                new
                {
                    clientRequestId,
                    habitId = habit!.Id,
                    completionDate = DateTime.UtcNow,
                    amount = 2
                }
            },
            bookReadingLogs = new object[0]
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BatchResultResponse>();

        Assert.Single(result!.HabitCompletions);
        Assert.True(result.HabitCompletions[0].Success);
    }

    [Fact]
    public async Task Batch_sync_habit_completion_for_unknown_habit_returns_failure_item()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/sync/batch", new
        {
            habitCompletions = new[]
            {
                new
                {
                    clientRequestId = Guid.NewGuid().ToString("N"),
                    habitId = 999999,
                    completionDate = DateTime.UtcNow,
                    amount = 1
                }
            },
            bookReadingLogs = new object[0]
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BatchResultResponse>();

        Assert.Single(result!.HabitCompletions);
        Assert.False(result.HabitCompletions[0].Success);
    }

    private sealed class HabitResponse
    {
        public int Id { get; set; }
    }

    private sealed class BatchResultResponse
    {
        public List<BatchItemResponse> HabitCompletions { get; set; } = new();
        public List<BatchItemResponse> BookReadingLogs { get; set; } = new();
    }

    private sealed class BatchItemResponse
    {
        public string ClientRequestId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int? CreatedId { get; set; }
        public string? Error { get; set; }
    }
}