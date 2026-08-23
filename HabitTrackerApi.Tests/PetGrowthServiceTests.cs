using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Configuration;
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

    private static (PetGrowthService Service, AppDbContext Context) CreateService(
        string dbName, int maxPetLevel = 100)
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
        var cosmetics = new PetCosmeticsService(context);
        var notifications = new NotificationService(context, new NoOpPushSender());
        var limits = Options.Create(new AppLimitsOptions { MaxPetLevel = maxPetLevel });
        var service = new PetGrowthService(context, notifications, cosmetics, limits);
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
    public async Task AddFocusXpAsync_EggStage_LevelStaysZeroRegardlessOfXp()
    {
        // Egg aşamasındayken PetLeveling.Apply hiçbir şey yapmamalı (Level
        // sadece Hatched pet'lerde hesaplanır).
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Egg, Xp = 0, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        // 10 dakika = 20 xp, hatch eşiğinin (30) altında; hâlâ Egg kalmalı.
        var pets = await service.AddFocusXpAsync("user-1", 10);

        var pet = pets.Single();
        Assert.Equal(PetStage.Egg, pet.Stage);
        Assert.Equal(0, pet.Level);
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
        Assert.Equal(0, pet.Level);
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

    // --- YENİ: MaxPetLevel tavanı testleri ---

    [Fact]
    public async Task AddStreakBonusXpAsync_ExceedingMaxLevel_LevelIsCappedAtMax()
    {
        // MaxPetLevel = 3 -> maxXp = 300. 1000 xp bonus eklense bile
        // Xp 300'e, Level 3'e kilitlenmeli.
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"), maxPetLevel: 3);
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Hatched, Xp = 0, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var pets = await service.AddStreakBonusXpAsync("user-1", 1000);

        var pet = pets.Single();
        Assert.Equal(300, pet.Xp);
        Assert.Equal(3, pet.Level);
    }

    [Fact]
    public async Task AddFocusXpAsync_RepeatedCallsNearCap_NeverExceedsMaxLevel()
    {
        // Art arda küçük artışlarla da tavanın aşılmadığını doğruluyoruz
        // (seri istek/farm senaryosunun simülasyonu).
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"), maxPetLevel: 2);
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Hatched, Xp = 190, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        // Her çağrıda 5 dakika * 2 = 10 xp; birkaç kez art arda çağırıyoruz.
        for (var i = 0; i < 5; i++)
        {
            await service.AddFocusXpAsync("user-1", 5);
        }

        var pet = await context.Pets.SingleAsync(p => p.UserId == "user-1");
        Assert.Equal(200, pet.Xp);   // maxLevel(2) * 100
        Assert.Equal(2, pet.Level);
    }

    [Fact]
    public async Task AddStreakBonusXpAsync_MultiplePetsAllRespectCap()
    {
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"), maxPetLevel: 1);
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Hatched, Xp = 50, CreatedAt = DateTime.UtcNow });
        context.Pets.Add(new Pet { UserId = "user-1", Type = "Dog", Stage = PetStage.Hatched, Xp = 90, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var pets = await service.AddStreakBonusXpAsync("user-1", 500);

        Assert.All(pets, p =>
        {
            Assert.Equal(100, p.Xp); // maxLevel(1) * 100
            Assert.Equal(1, p.Level);
        });
    }

    [Fact]
    public async Task RemoveStreakBonusXpAsync_AfterCapReached_LevelRecalculatedCorrectly()
    {
        // Tavana ulaşmış bir pet'ten XP düşüldüğünde Level de doğru şekilde
        // (tavanın altına) yeniden hesaplanmalı.
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"), maxPetLevel: 2);
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Hatched, Xp = 200, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        await service.RemoveStreakBonusXpAsync("user-1", 150);

        var pet = await context.Pets.SingleAsync(p => p.UserId == "user-1");
        Assert.Equal(50, pet.Xp);
        Assert.Equal(0, pet.Level);
    }

    [Fact]
    public async Task AddFocusXpAsync_DefaultMaxLevel_AllowsGrowthWellBelowCap()
    {
        // Varsayılan tavan (100) ile normal senaryoda hiçbir kırpma
        // olmadığını doğrulayan regresyon testi.
        var (service, context) = CreateService(Guid.NewGuid().ToString("N"));
        await using var _ = context;

        context.Pets.Add(new Pet { UserId = "user-1", Type = "Cat", Stage = PetStage.Hatched, Xp = 0, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var pets = await service.AddFocusXpAsync("user-1", 20); // 40 xp

        var pet = pets.Single();
        Assert.Equal(40, pet.Xp);
        Assert.Equal(0, pet.Level); // 40/100 = 0, henüz level atlamadı
    }
}