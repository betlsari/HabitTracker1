using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Data;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class RecalculationIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RecalculationIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Updating_a_book_enqueues_and_completes_recalculation_job()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/books", new
        {
            title = $"Recalculation book {Guid.NewGuid():N}",
            goalType = "Pages",
            dailyGoalAmount = 10,
            totalPages = 100
        });
        create.EnsureSuccessStatusCode();
        var book = await create.Content.ReadFromJsonAsync<BookDtoShape>();

        var update = await client.PutAsJsonAsync($"/api/books/{book!.Id}", new
        {
            title = book.Title,
            goalType = "Pages",
            dailyGoalAmount = 20,
            totalPages = 100
        });
        update.EnsureSuccessStatusCode();

        var processed = await ProcessPendingJobsAsync();

        Assert.True(processed >= 1);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = db.RecalculationOutboxItems.Single(x => x.BookId == book.Id);
        Assert.Equal(RecalculationOutboxStatus.Completed, job.Status);
    }

    [Fact]
    public async Task Updating_a_habit_enqueues_and_completes_recalculation_job()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/habits", new
        {
            name = $"Recalculation habit {Guid.NewGuid():N}",
            category = "Diğer",
            customCategoryName = "Testing",
            unit = "Count",
            dailyGoal = 1,
            period = "Daily"
        });
        create.EnsureSuccessStatusCode();
        var habit = await create.Content.ReadFromJsonAsync<HabitDtoShape>();

        var update = await client.PutAsJsonAsync($"/api/habits/{habit!.Id}", new
        {
            name = habit.Name,
            category = "Diğer",
            customCategoryName = "Testing",
            unit = "Count",
            dailyGoal = 2,
            period = "Daily"
        });
        update.EnsureSuccessStatusCode();

        var processed = await ProcessPendingJobsAsync();

        Assert.True(processed >= 1);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = db.RecalculationOutboxItems.Single(x => x.HabitId == habit.Id);
        Assert.Equal(RecalculationOutboxStatus.Completed, job.Status);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync());
        return client;
    }

    private async Task<int> ProcessPendingJobsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var worker = new RecalculationBackgroundService(
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RecalculationBackgroundService>>());
        return await worker.ProcessPendingBatchAsync();
    }

    private sealed class BookDtoShape
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private sealed class HabitDtoShape
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}