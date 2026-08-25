using Data;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class FlowerServiceTests
{
    // DÜZELTİLDİ: IPushNotificationSender yerine IPushQueue kullanılıyor.
    private sealed class NoOpPushQueue : IPushQueue
    {
        public Task EnqueueAsync(string userId, string title, string body, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static (FlowerService Service, AppDbContext Context) CreateService(string dbName)
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
        var cosmetics = new PetCosmeticsService(context);
        var notifications = new NotificationService(context, new NoOpPushQueue());
        var service = new FlowerService(context, notifications, cosmetics);
        return (service, context);
    }

    [Fact]
    public async Task AddWaterAsync_CreatesFlowerAndAccumulatesWater()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        var flower = await service.AddWaterAsync("user-1", 7);

        Assert.Equal(7, flower.WaterAmount);
        Assert.Equal(0, flower.Level);
    }

    [Fact]
    public async Task AddWaterAsync_LevelsUpEveryTenUnits()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        var flower = await service.AddWaterAsync("user-1", 25);

        Assert.Equal(2, flower.Level); // 25 / 10 (UnitsPerLevel) = 2
    }

    [Fact]
    public async Task AddWaterAsync_NegativeAmount_NeverGoesBelowZero()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        await service.AddWaterAsync("user-1", 5);
        var flower = await service.AddWaterAsync("user-1", -100);

        Assert.Equal(0, flower.WaterAmount);
    }

    [Theory]
    [InlineData(0, "Seed")]
    [InlineData(1, "Seedling")]
    [InlineData(5, "Sprout")]
    [InlineData(10, "Bloom")]
    public void StageName_ReturnsExpectedStage(int level, string expected)
    {
        Assert.Equal(expected, FlowerService.StageName(level));
    }
}