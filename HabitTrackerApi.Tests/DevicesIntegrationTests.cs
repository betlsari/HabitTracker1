using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class DevicesIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public DevicesIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_NewToken_Succeeds()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/devices", new
        {
            token = "device-token-" + Guid.NewGuid().ToString("N"),
            platform = "ios"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_SameTokenTwice_UpdatesInsteadOfDuplicating()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var deviceToken = "device-token-" + Guid.NewGuid().ToString("N");

        await client.PostAsJsonAsync("/api/devices", new { token = deviceToken, platform = "ios" });
        await client.PostAsJsonAsync("/api/devices", new { token = deviceToken, platform = "android" });

        var listResponse = await client.GetAsync("/api/devices");
        var result = await listResponse.Content.ReadFromJsonAsync<PagedResponse>();

        Assert.Single(result!.Items.Where(i => i.TokenSuffix == deviceToken[^4..]));
    }

    [Fact]
    public async Task Unregister_ByToken_RemovesDevice()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var deviceToken = "device-token-" + Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/devices", new { token = deviceToken, platform = "ios" });

        var deleteResponse = await client.DeleteAsync($"/api/devices?token={deviceToken}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Unregister_UnknownToken_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.DeleteAsync("/api/devices?token=nonexistent-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class PagedResponse
    {
        public List<DeviceItem> Items { get; set; } = new();
    }

    private sealed class DeviceItem
    {
        public string TokenSuffix { get; set; } = string.Empty;
    }
}
