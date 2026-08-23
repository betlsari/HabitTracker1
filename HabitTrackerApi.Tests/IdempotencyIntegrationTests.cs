using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class IdempotencyIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public IdempotencyIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Duplicate_habit_completion_with_same_ClientRequestId_is_not_double_counted()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createHabit = await client.PostAsJsonAsync("/api/habits", new
        {
            name = "Idempotency habit",
            category = "Su",
            dailyGoal = 5,
            period = "Daily"
        });
        createHabit.EnsureSuccessStatusCode();
        var habit = await createHabit.Content.ReadFromJsonAsync<HabitResponse>();

        var clientRequestId = Guid.NewGuid().ToString("N");
        var payload = new
        {
            completionDate = DateTime.UtcNow,
            amount = 2,
            clientRequestId
        };

        var first = await client.PostAsJsonAsync($"/api/habits/{habit!.Id}/habitcompletions", payload);
        var second = await client.PostAsJsonAsync($"/api/habits/{habit.Id}/habitcompletions", payload);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var listResponse = await client.GetAsync($"/api/habits/{habit.Id}/habitcompletions");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<PagedCompletions>();

        Assert.Equal(1, list!.TotalCount);
    }

    [Fact]
    public async Task Duplicate_book_reading_log_with_same_ClientRequestId_is_not_double_counted()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createBook = await client.PostAsJsonAsync("/api/books", new
        {
            title = "Idempotency Book",
            goalType = "Pages",
            dailyGoalAmount = 10,
            totalPages = 200
        });
        createBook.EnsureSuccessStatusCode();
        var book = await createBook.Content.ReadFromJsonAsync<BookResponse>();

        var clientRequestId = Guid.NewGuid().ToString("N");
        var payload = new
        {
            readDate = DateTime.UtcNow,
            amount = 5,
            clientRequestId
        };

        var first = await client.PostAsJsonAsync($"/api/books/{book!.Id}/reading-logs", payload);
        var second = await client.PostAsJsonAsync($"/api/books/{book.Id}/reading-logs", payload);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var listResponse = await client.GetAsync($"/api/books/{book.Id}/reading-logs");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<PagedReadingLogs>();

        Assert.Equal(1, list!.TotalCount);
    }

    private sealed class HabitResponse
    {
        public int Id { get; set; }
    }

    private sealed class BookResponse
    {
        public int Id { get; set; }
    }

    private sealed class PagedCompletions
    {
        public int TotalCount { get; set; }
    }

    private sealed class PagedReadingLogs
    {
        public int TotalCount { get; set; }
    }
}