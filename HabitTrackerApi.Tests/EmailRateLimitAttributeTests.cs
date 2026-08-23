using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class EmailRateLimitAttributeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public EmailRateLimitAttributeTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ForgotPassword_ExceedingPerEmailLimit_Returns429()
    {
        using var client = _factory.CreateClient();
        var email = $"ratelimit-{Guid.NewGuid():N}@example.test";

        HttpResponseMessage? last = null;
        
        for (var i = 0; i < 10; i++)
        {
            last = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
            if (last.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }
}