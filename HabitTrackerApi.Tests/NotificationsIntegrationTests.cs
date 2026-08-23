using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class NotificationsIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public NotificationsIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Authenticated_user_can_list_notifications()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/notifications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Marking_nonexistent_notification_read_returns_not_found()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/notifications/999999/read", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}