using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite; // EKLENDİ: SqliteConnection için gerekli
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
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;

namespace HabitTrackerApi.Tests;
public class SqliteCustomFunctionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqliteConnection sqliteConn)
        {
            sqliteConn.CreateFunction<string, int>("hashtext", text => text != null ? text.GetHashCode() : 0);
        }
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (connection is SqliteConnection sqliteConn)
        {
            sqliteConn.CreateFunction<string, int>("hashtext", text => text != null ? text.GetHashCode() : 0);
        }
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
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
    private SqliteConnection _connection; 

    public ApiFactory()
{
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    
    
    _connection.CreateFunction("hashtext", (string text) => text != null ? text.GetHashCode() : 0);
}

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

            services.AddDbContext<AppDbContext>(options => 
{
    options.UseSqlite(_connection);
    
    
    options.AddInterceptors(new SqliteCustomFunctionInterceptor());
});
        });
    }

        protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }

    public async Task<string> CreateAccessTokenAsync()
    {
        using var scope = Services.CreateScope();
        
        // EKLENDİ: Veritabanının her test koşusunda (veya token üretiminde) 
        // oluşturulduğundan emin oluyoruz.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        var email = $"test-{Guid.NewGuid():N}@example.test";
        var user = new User { UserName = email, Email = email, EmailConfirmed = true, CreatedAt = DateTime.UtcNow };
        var result = await users.CreateAsync(user, "Integration1A");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return tokenService.GenerateToken(user);
    }
}