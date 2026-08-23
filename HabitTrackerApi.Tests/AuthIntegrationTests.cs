using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class AuthIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_with_mismatched_passwords_fails()
    {
        using var client = _factory.CreateClient();
        var email = $"test-{Guid.NewGuid():N}@example.test";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Integration1A",
            confirmPassword = "DifferentPass1A"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_unknown_user_returns_unauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "nonexistent@example.test",
            password = "whatever"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}