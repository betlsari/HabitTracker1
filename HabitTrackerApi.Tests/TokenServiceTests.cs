using Microsoft.Extensions.Configuration;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class TokenServiceTests
{
    private static TokenService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "unit-test-key-with-at-least-thirty-two-bytes!!",
                ["Jwt:Issuer"] = "HabitTrackerApi",
                ["Jwt:Audience"] = "HabitTrackerApiUsers"
            })
            .Build();

        return new TokenService(config);
    }

    private static User CreateUser() => new()
    {
        Id = "user-1",
        Email = "test@example.test",
        UserName = "test@example.test"
    };

    [Fact]
    public void GenerateToken_ProducesNonEmptyJwt()
    {
        var service = CreateService();
        var token = service.GenerateToken(CreateUser());

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Contains('.', token);
    }

    [Fact]
    public void HashToken_SameInput_ProducesSameHash()
    {
        var hash1 = TokenService.HashToken("some-refresh-token");
        var hash2 = TokenService.HashToken("some-refresh-token");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GenerateRefreshToken_ProducesUniqueValues()
    {
        var service = CreateService();

        var token1 = service.GenerateRefreshToken();
        var token2 = service.GenerateRefreshToken();

        Assert.NotEqual(token1, token2);
    }
}