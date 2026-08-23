using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class NotificationPreferencesIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public NotificationPreferencesIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_WithNoPreferenceSaved_ReturnsDefaults()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/notificationpreferences");

        response.EnsureSuccessStatusCode();
        var pref = await response.Content.ReadFromJsonAsync<PreferenceResponse>();
        Assert.Empty(pref!.DisabledTypes);
        Assert.Null(pref.QuietHoursStart);
    }

    [Fact]
    public async Task Put_SavesPreferences_AndGetReturnsThem()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var putResponse = await client.PutAsJsonAsync("/api/notificationpreferences", new
        {
            disabledTypes = new[] { "Reminder" },
            quietHoursStart = "22:00",
            quietHoursEnd = "08:00"
        });
        putResponse.EnsureSuccessStatusCode();

        var getResponse = await client.GetAsync("/api/notificationpreferences");
        var pref = await getResponse.Content.ReadFromJsonAsync<PreferenceResponse>();

        Assert.Contains("Reminder", pref!.DisabledTypes);
    }

    [Fact]
    public async Task Put_InvalidType_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync("/api/notificationpreferences", new
        {
            disabledTypes = new[] { "NotARealType" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ResetsPreferenceToDefault()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await client.PutAsJsonAsync("/api/notificationpreferences", new { disabledTypes = new[] { "Reminder" } });

        var deleteResponse = await client.DeleteAsync("/api/notificationpreferences");
        deleteResponse.EnsureSuccessStatusCode();

        var pref = await deleteResponse.Content.ReadFromJsonAsync<PreferenceResponse>();
        Assert.Empty(pref!.DisabledTypes);
    }

    private sealed class PreferenceResponse
    {
        public List<string> DisabledTypes { get; set; } = new();
        public TimeOnly? QuietHoursStart { get; set; }
        public TimeOnly? QuietHoursEnd { get; set; }
    }
}