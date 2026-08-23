using Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class AuthAuditServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static DefaultHttpContext CreateHttpContext(string? userAgent = "test-agent")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        if (userAgent != null)
        {
            context.Request.Headers.UserAgent = userAgent;
        }
        return context;
    }

    [Fact]
    public async Task RecordAsync_WithUser_PersistsEventWithUserIdAndEmail()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new AuthAuditService(context);

        var user = new User { Id = "user-1", Email = "test@example.test", UserName = "test@example.test" };

        await service.RecordAsync(CreateHttpContext(), "login", true, user);

        var stored = await context.AuthAuditEvents.SingleAsync();
        Assert.Equal("user-1", stored.UserId);
        Assert.Equal("test@example.test", stored.Email);
        Assert.Equal("login", stored.EventType);
        Assert.True(stored.Succeeded);
        Assert.Equal("127.0.0.1", stored.IpAddress);
        Assert.Equal("test-agent", stored.UserAgent);
    }

    [Fact]
    public async Task RecordAsync_WithoutUser_UsesProvidedEmail()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new AuthAuditService(context);

        await service.RecordAsync(CreateHttpContext(), "login", false, email: "unknown@example.test", detail: "unknown-user");

        var stored = await context.AuthAuditEvents.SingleAsync();
        Assert.Null(stored.UserId);
        Assert.Equal("unknown@example.test", stored.Email);
        Assert.False(stored.Succeeded);
        Assert.Equal("unknown-user", stored.Detail);
    }

    [Fact]
    public async Task RecordAsync_NoUserNoEmail_StoresEmptyEmail()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new AuthAuditService(context);

        await service.RecordAsync(CreateHttpContext(), "some-event", true);

        var stored = await context.AuthAuditEvents.SingleAsync();
        Assert.Equal(string.Empty, stored.Email);
    }

    [Fact]
    public async Task RecordAsync_MultipleEvents_AreAllPersisted()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new AuthAuditService(context);

        await service.RecordAsync(CreateHttpContext(), "login", true, email: "a@example.test");
        await service.RecordAsync(CreateHttpContext(), "logout", true, email: "a@example.test");

        Assert.Equal(2, await context.AuthAuditEvents.CountAsync());
    }
}