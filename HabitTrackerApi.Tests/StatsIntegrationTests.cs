using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class StatsIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public StatsIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMonthlySummary_DefaultParams_ReturnsTwelveMonths()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/stats/monthly");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MonthlySummaryResponse>();
        Assert.Equal(12, result!.Months.Count);
    }

    [Fact]
    public async Task GetMonthlySummary_InvalidMonthsBack_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/stats/monthly?monthsBack=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMonthlySummary_CustomMonthsBack_ReturnsRequestedCount()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/stats/monthly?monthsBack=3");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MonthlySummaryResponse>();
        Assert.Equal(3, result!.Months.Count);
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/stats/monthly");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class MonthlySummaryResponse
    {
        public List<object> Months { get; set; } = new();
    }
}