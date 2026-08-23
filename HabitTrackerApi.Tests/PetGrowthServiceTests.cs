using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class PetGrowthServiceTests
{
    private sealed class NoOpPushSender : IPushNotificationSender
    {
        public Task SendAsync(IReadOnlyList<string> deviceTokens, string title, string body, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static (PetGrowthService Service, AppDbContext Context) CreateService(string dbName)
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
        var cosmetics = new PetCosmeticsService(context);
        var notifications = new NotificationService(context, new NoOpPushSender());
        var service = new PetGrowthService(context, notifications, cosmetics);
        return (service, context);
    }

    [Fact]
    public async Task AddFocusXpAsync_NoPets_ReturnsEmptyList()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        var result = await service.AddFocusXpAsync("user-1", 10);

        Assert.Empty(result);
    }

    [Fact]
    public async Task AddFocusXpAsync_AddsXpToAllUserPets()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Egg, CreatedAt = DateTime.UtcNow });
        context.Pets.Add(new Pet { UserId = "user-1", Type = "Dog", Stage = PetStage.Egg, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var pets = await service.AddFocusXpAsync("user-1", 5); // 5 dakika * 2 = 10 xp

        Assert.All(pets, p => Assert.Equal(10, p.Xp));
    }

    [Fact]
    public async Task AddFocusXpAsync_ReachingHatchThreshold_HatchesEgg()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Egg, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        // PetHatching.HatchXpThreshold = 30, XpPerFocusMinute = 2 -> 15 dakika yeterli
        var pets = await service.AddFocusXpAsync("user-1", 15);

        Assert.Equal(PetStage.Hatched, pets.Single().Stage);
    }

    [Fact]
    public async Task RemoveFocusXpAsync_NeverGoesBelowZero()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Hatched, Xp = 5, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        await service.RemoveFocusXpAsync("user-1", 100); // 100*2 = 200 xp kaybı istenir

        var pet = await context.Pets.SingleAsync(p => p.UserId == "user-1");
        Assert.Equal(0, pet.Xp);
    }

    [Fact]
    public async Task AddStreakBonusXpAsync_AddsBonusDirectly()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Hatched, Xp = 0, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var pets = await service.AddStreakBonusXpAsync("user-1", 8);

        Assert.Equal(8, pets.Single().Xp);
    }

    [Fact]
    public async Task RemoveStreakBonusXpAsync_SubtractsBonus()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Hatched, Xp = 20, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        await service.RemoveStreakBonusXpAsync("user-1", 8);

        var pet = await context.Pets.SingleAsync(p => p.UserId == "user-1");
        Assert.Equal(12, pet.Xp);
    }
}