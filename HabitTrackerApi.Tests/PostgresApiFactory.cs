using System.Net.Http.Headers;
using System.Net.Http.Json;
using Data;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Models;
using Services;
using Testcontainers.PostgreSql;
using Xunit;
using Microsoft.Extensions.Configuration;


namespace HabitTrackerApi.Tests;


public sealed class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("habittracker_test")
        .WithUsername("habittracker")
        .WithPassword("habittracker")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

   protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.UseEnvironment("Testing");

    builder.ConfigureAppConfiguration((_, configuration) =>
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "postgres-concurrency-test-key-at-least-32-bytes!!",
            ["Jwt:Issuer"] = "HabitTrackerApi",
            ["Jwt:Audience"] = "HabitTrackerApiUsers"
        });
    });

    builder.ConfigureServices(services =>
    {
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.RemoveAll<AppDbContext>();
        services.RemoveAll<IHostedService>();

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(_postgres.GetConnectionString()));
    });
}
        
    

    public async Task<string> CreateAccessTokenAsync()
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        var email = $"pgtest-{Guid.NewGuid():N}@example.test";
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await users.CreateAsync(user, "Integration1A");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));

        return tokenService.GenerateToken(user);
    }

    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}