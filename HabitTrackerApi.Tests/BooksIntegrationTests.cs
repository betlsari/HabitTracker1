using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class BooksIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public BooksIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Authenticated_user_can_create_and_fetch_a_book()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync("/api/books", new
        {
            title = "Integration Book",
            goalType = "Pages",
            dailyGoalAmount = 10,
            totalPages = 200
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
    }

    [Fact]
    public async Task Repeating_create_request_with_same_client_id_returns_one_book()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync());
        var request = new
        {
            title = $"Idempotent book {Guid.NewGuid():N}",
            goalType = "Pages",
            dailyGoalAmount = 10,
            totalPages = 100,
            clientRequestId = Guid.NewGuid().ToString("N")
        };

        var first = await client.PostAsJsonAsync("/api/books", request);
        var second = await client.PostAsJsonAsync("/api/books", request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBook = await first.Content.ReadFromJsonAsync<BookIdResponse>();
        var secondBook = await second.Content.ReadFromJsonAsync<BookIdResponse>();
        Assert.Equal(firstBook!.Id, secondBook!.Id);
    }

    private sealed class BookIdResponse
    {
        public int Id { get; set; }
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}