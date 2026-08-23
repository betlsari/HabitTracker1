using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Data;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Models;
using Services;

using Xunit;

namespace HabitTrackerApi.Tests;


// ============================================================
// SQLITE CUSTOM FUNCTIONS
// ============================================================

public class SqliteCustomFunctionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        RegisterFunctions(connection);

        base.ConnectionOpened(
            connection,
            eventData);
    }


    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RegisterFunctions(connection);

        await base.ConnectionOpenedAsync(
            connection,
            eventData,
            cancellationToken);
    }


    private static void RegisterFunctions(
        DbConnection connection)
    {
        if (connection is not SqliteConnection sqliteConn)
        {
            return;
        }


        // PostgreSQL hashtext() fonksiyonunun
        // SQLite testlerindeki karşılığı.
        sqliteConn.CreateFunction<string, int>(
            "hashtext",
            text => text != null
                ? text.GetHashCode()
                : 0);


        // PostgreSQL advisory lock fonksiyonunun
        // SQLite testlerindeki no-op karşılığı.
        //
        // Gerçek PostgreSQL'de transaction süresince
        // advisory lock alınır.
        //
        // SQLite integration testlerinde gerçek bir
        // advisory lock'a ihtiyacımız olmadığı için
        // sadece SQL'in çalışmasını sağlayan stub
        // kullanıyoruz.
        sqliteConn.CreateFunction<int, int>(
            "pg_advisory_xact_lock",
            _ => 0);
    }
}


// ============================================================
// API INTEGRATION TESTS
// ============================================================

public sealed class ApiIntegrationTests
    : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;


    public ApiIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }


    [Fact]
    public async Task Authenticated_user_can_create_a_habit_end_to_end()
    {
        using var client =
            _factory.CreateClient();


        var token =
            await _factory.CreateAccessTokenAsync();


        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);


        var response =
            await client.PostAsJsonAsync(
                "/api/habits",
                new
                {
                    name = "Integration habit",
                    category = "Su",
                    dailyGoal = 1,
                    period = "Daily"
                });


        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
    }
}


// ============================================================
// API FACTORY
// ============================================================

public sealed class ApiFactory
    : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;


    public ApiFactory()
    {
        // SQLite in-memory database.
        //
        // Connection açık kaldığı sürece database yaşamaya devam eder.
        _connection =
            new SqliteConnection(
                "DataSource=:memory:");

        _connection.Open();


        // PostgreSQL hashtext() fonksiyonu.
        _connection.CreateFunction(
            "hashtext",
            (string text) =>
                text != null
                    ? text.GetHashCode()
                    : 0);


        // PostgreSQL advisory lock fonksiyonu.
        //
        // SQLite testlerinde no-op.
        _connection.CreateFunction(
            "pg_advisory_xact_lock",
            (int _) => 0);
    }


    // ========================================================
    // WEB HOST CONFIGURATION
    // ========================================================

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");


        // Test sırasında gereksiz logları kapat.
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
        });


        // Test JWT ayarları.
        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:Key"] =
                            "integration-test-key-with-at-least-thirty-two-bytes",

                        ["Jwt:Issuer"] =
                            "HabitTrackerApi",

                        ["Jwt:Audience"] =
                            "HabitTrackerApiUsers"
                    });
            });


        builder.ConfigureServices(
            services =>
            {
                // Production DbContext kayıtlarını kaldır.
                services.RemoveAll<
                    DbContextOptions<AppDbContext>>();

                services.RemoveAll<
                    IDbContextOptionsConfiguration<AppDbContext>>();

                services.RemoveAll<AppDbContext>();


                // Testlerde background service çalıştırma.
                services.RemoveAll<IHostedService>();


                // SQLite DbContext.
                services.AddDbContext<AppDbContext>(
                    options =>
                    {
                        options.UseSqlite(
                            _connection);


                        options.AddInterceptors(
                            new SqliteCustomFunctionInterceptor());
                    });
            });
    }


    // ========================================================
    // HOST OLUŞTURULDUĞUNDA DATABASE OLUŞTUR
    // ========================================================

    protected override IHost CreateHost(
        IHostBuilder builder)
    {
        var host =
            base.CreateHost(builder);


        using var scope =
            host.Services.CreateScope();


        var db =
            scope.ServiceProvider
                .GetRequiredService<AppDbContext>();


        // SQLite in-memory database'in tablolarını oluştur.
        db.Database.EnsureCreated();


        return host;
    }


    // ========================================================
    // ACCESS TOKEN OLUŞTUR
    // ========================================================

    public async Task<string> CreateAccessTokenAsync()
    {
        var result =
            await CreateAccessTokenWithEmailAsync();


        return result.Token;
    }


    // ========================================================
    // EMAIL + ACCESS TOKEN OLUŞTUR
    // ========================================================

    public async Task<(string Email, string Token)>
        CreateAccessTokenWithEmailAsync()
    {
        using var scope =
            Services.CreateScope();


        var db =
            scope.ServiceProvider
                .GetRequiredService<AppDbContext>();


        // Database'in hazır olduğundan emin ol.
        db.Database.EnsureCreated();


        var users =
            scope.ServiceProvider
                .GetRequiredService<UserManager<User>>();


        var tokenService =
            scope.ServiceProvider
                .GetRequiredService<TokenService>();


        // Her test için benzersiz email.
        var email =
            $"test-{Guid.NewGuid():N}@example.test";


        // Test kullanıcısı.
        var user =
            new User
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };


        // Kullanıcıyı oluştur.
        var result =
            await users.CreateAsync(
                user,
                "Integration1A");


        Assert.True(
            result.Succeeded,
            string.Join(
                ", ",
                result.Errors.Select(
                    e => e.Description)));


        // JWT oluştur.
        var token =
            tokenService.GenerateToken(user);


        // Email + Token birlikte dönüyor.
        return (email, token);
    }


    // ========================================================
    // DISPOSE
    // ========================================================

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            _connection.Close();
            _connection.Dispose();
        }


        base.Dispose(disposing);
    }
}