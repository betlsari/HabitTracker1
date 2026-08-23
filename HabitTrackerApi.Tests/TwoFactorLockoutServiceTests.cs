using Data;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class TwoFactorLockoutServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    [Fact]
    public async Task IsLockedOutAsync_NoAttempts_ReturnsFalse()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new TwoFactorLockoutService(context);

        var lockedOut = await service.IsLockedOutAsync("user-1");

        Assert.False(lockedOut);
    }

    [Fact]
    public async Task RecordFailureAsync_BelowThreshold_DoesNotLockOut()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new TwoFactorLockoutService(context);

        for (var i = 0; i < 4; i++)
        {
            await service.RecordFailureAsync("user-1");
        }

        Assert.False(await service.IsLockedOutAsync("user-1"));
    }

    [Fact]
    public async Task RecordFailureAsync_ReachesThreshold_LocksOut()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new TwoFactorLockoutService(context);

        for (var i = 0; i < 5; i++)
        {
            await service.RecordFailureAsync("user-1");
        }

        Assert.True(await service.IsLockedOutAsync("user-1"));
    }

    [Fact]
    public async Task ResetAsync_ClearsLockout()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new TwoFactorLockoutService(context);

        for (var i = 0; i < 5; i++)
        {
            await service.RecordFailureAsync("user-1");
        }
        Assert.True(await service.IsLockedOutAsync("user-1"));

        await service.ResetAsync("user-1");

        Assert.False(await service.IsLockedOutAsync("user-1"));
    }

    [Fact]
    public async Task RecordFailureAsync_DifferentUsers_AreIndependent()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new TwoFactorLockoutService(context);

        for (var i = 0; i < 5; i++)
        {
            await service.RecordFailureAsync("user-1");
        }

        Assert.True(await service.IsLockedOutAsync("user-1"));
        Assert.False(await service.IsLockedOutAsync("user-2"));
    }
}