using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class BackgroundsIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public BackgroundsIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetBackgrounds_Default_HomeIsUnlockedAndEquipped()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/backgrounds");
        response.EnsureSuccessStatusCode();

        var backgrounds = await response.Content.ReadFromJsonAsync<List<BackgroundItem>>();
        var home = backgrounds!.Single(b => b.Code == "Home");

        Assert.True(home.Unlocked);
        Assert.True(home.Equipped);
    }

    [Fact]
    public async Task Equip_LockedBackground_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync("/api/backgrounds/equip", new { background = "Forest" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Equip_HomeBackground_Succeeds()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync("/api/backgrounds/equip", new { background = "Home" });

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/backgrounds");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class BackgroundItem
    {
        public string Code { get; set; } = string.Empty;
        public bool Unlocked { get; set; }
        public bool Equipped { get; set; }
    }
}