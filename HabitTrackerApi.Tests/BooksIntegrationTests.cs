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
    public async Task Unauthenticated_request_is_rejected()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}