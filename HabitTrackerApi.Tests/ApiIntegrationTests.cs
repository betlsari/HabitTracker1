using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class ApiIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ApiIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Authenticated_user_can_create_a_habit_end_to_end()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/habits", new
        {
            name = "Integration habit", category = "Su", dailyGoal = 1, period = "Daily"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "integration-test-key-with-at-least-thirty-two-bytes",
            ["Jwt:Issuer"] = "HabitTrackerApi",
            ["Jwt:Audience"] = "HabitTrackerApiUsers"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IHostedService>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("habit-api-integration-tests"));
        });
    }

    public async Task<string> CreateAccessTokenAsync()
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        var email = $"test-{Guid.NewGuid():N}@example.test";
        var user = new User { UserName = email, Email = email, EmailConfirmed = true, CreatedAt = DateTime.UtcNow };
        var result = await users.CreateAsync(user, "Integration1A");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return tokenService.GenerateToken(user);
    }
}
