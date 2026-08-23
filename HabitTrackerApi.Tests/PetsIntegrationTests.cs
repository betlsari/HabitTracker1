using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class PetsIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public PetsIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Authenticated_user_can_create_a_pet()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/pets", new { type = "Cat" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_pet_type_is_rejected()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/pets", new { type = "Dragon" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}