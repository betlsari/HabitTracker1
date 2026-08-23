using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class PetCosmeticsServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    [Fact]
    public async Task EvaluateAccessoryUnlocksAsync_UnlocksHatAtLevelThree()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new PetCosmeticsService(context);

        var pet = new Pet
        {
            UserId = "user-1",
            Type = "Cat",
            Stage = PetStage.Hatched,
            Level = 3,
            CreatedAt = DateTime.UtcNow
        };
        context.Pets.Add(pet);
        await context.SaveChangesAsync();

        await service.EvaluateAccessoryUnlocksAsync(pet);

        var unlocked = await context.PetAccessoryUnlocks.Where(u => u.PetId == pet.Id).ToListAsync();
        Assert.Contains(unlocked, u => u.Accessory == PetAccessories.Hat);
    }

    [Fact]
    public async Task TryEquipAccessoryAsync_FailsWhenNotUnlocked()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new PetCosmeticsService(context);

        var pet = new Pet
        {
            UserId = "user-1",
            Type = "Cat",
            Stage = PetStage.Hatched,
            Level = 0,
            CreatedAt = DateTime.UtcNow
        };
        context.Pets.Add(pet);
        await context.SaveChangesAsync();

        var result = await service.TryEquipAccessoryAsync(pet, PetAccessories.Hat);

        Assert.False(result);
    }

    [Fact]
    public async Task TryEquipAccessoryAsync_SucceedsAfterUnlock()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new PetCosmeticsService(context);

        var pet = new Pet
        {
            UserId = "user-1",
            Type = "Cat",
            Stage = PetStage.Hatched,
            Level = 3,
            CreatedAt = DateTime.UtcNow
        };
        context.Pets.Add(pet);
        await context.SaveChangesAsync();
        await service.EvaluateAccessoryUnlocksAsync(pet);

        var result = await service.TryEquipAccessoryAsync(pet, PetAccessories.Hat);

        Assert.True(result);
        Assert.Equal(PetAccessories.Hat, pet.EquippedAccessory);
    }

    [Fact]
    public async Task EvaluateBackgroundUnlocksAsync_UnlocksForestAtLevelFive()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new PetCosmeticsService(context);

        await service.EvaluateBackgroundUnlocksAsync("user-1", flowerLevel: 5);

        var unlocked = await context.UserBackgroundUnlocks.Where(u => u.UserId == "user-1").ToListAsync();
        Assert.Contains(unlocked, u => u.Background == PetBackgrounds.Forest);
    }
}