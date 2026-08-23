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

// DÜZELTİLDİ: Bu interceptor önceden sadece "hashtext" fonksiyonunu SQLite'a
// öğretiyordu. Ancak DevicesController/PetsController/BooksController/
// HabitsController/HabitCompletionsController hepsi
// "SELECT pg_advisory_xact_lock(hashtext(...))" çağırıyor — pg_advisory_xact_lock
// PostgreSQL'e özgü bir advisory-lock fonksiyonu ve SQLite'ta hiç yok. Bu da
// testlerde "no such function: pg_advisory_xact_lock" hatasıyla 500'e yol
// açıyordu (CreatePet, CreateHabit, CreateBook, DeviceToken Register, vb.
// birçok test bu yüzden başarısız oluyordu).
//
// SQLite testleri tek bağlantı üzerinden ve genelde ardışık çalıştığından,
// gerçek bir kilitleme semantiğine ihtiyaç yok; no-op bir stub yeterli ve
// güvenli. Fonksiyon tek bir int parametre alıp (hashtext'in döndürdüğü hash)
// anlamsız bir int dönüyor — SQL'deki "SELECT pg_advisory_xact_lock(...)"
// ifadesinin sözdizimsel olarak geçerli kalması için.
public class SqliteCustomFunctionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        RegisterFunctions(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        RegisterFunctions(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void RegisterFunctions(DbConnection connection)
    {
        if (connection is not SqliteConnection sqliteConn)
        {
            return;
        }

        sqliteConn.CreateFunction<string, int>("hashtext", text => text != null ? text.GetHashCode() : 0);

        // YENİ: pg_advisory_xact_lock için no-op stub. Gerçek Postgres'te bu
        // fonksiyon transaction süresince bir advisory lock alır; testlerde
        // sadece SQL'in parse edilip çalışabilmesi yeterli.
        sqliteConn.CreateFunction<int, int>("pg_advisory_xact_lock", _ => 0);
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
        // YENİ: Doğrudan bu bağlantı üzerinde de stub'ı tanımlıyoruz — bazı
        // akışlar (ör. CreateAccessTokenAsync içindeki EnsureCreated) DbContext
        // interceptor'ı devreye girmeden bu bağlantıyı kullanabiliyor.
        _connection.CreateFunction("pg_advisory_xact_lock", (int _) => 0);
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

    // DÜZELTİLDİ (🔴 "no such table: AspNetUsers"): Şema önceden sadece
    // CreateAccessTokenAsync() çağrıldığında (db.Database.EnsureCreated() ile)
    // oluşturuluyordu. Kimlik doğrulaması gerektirmeyen veya token almadan
    // doğrudan bir uç noktaya istek atan testler (ör.
    // EmailRateLimitAttributeTests.ForgotPassword_ExceedingPerEmailLimit_Returns429)
    // hiçbir tablo oluşturulmamış bir veritabanına karşı çalışıyor ve
    // "no such table: AspNetUsers" ile patlıyordu. Artık şema, host ilk
    // ayağa kalktığında (herhangi bir test çalışmadan önce) eager olarak
    // oluşturuluyor.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        return host;
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

        // NOT: Şema artık CreateHost() içinde eager olarak oluşturuluyor;
        // burada tekrar EnsureCreated() çağırmak zararsız (idempotent) ama
        // gereksiz olduğundan kaldırıldı.
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        var email = $"test-{Guid.NewGuid():N}@example.test";
        var user = new User { UserName = email, Email = email, EmailConfirmed = true, CreatedAt = DateTime.UtcNow };
        var result = await users.CreateAsync(user, "Integration1A");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return tokenService.GenerateToken(user);
    }
}