using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Xunit;

namespace HabitTrackerApi.Tests;

public class PetConcurrencyTests
{
    private static DbContextOptions<AppDbContext> BuildOptions(string dbName) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

    [Fact]
    public async Task Concurrent_pet_edits_are_detected_via_ConcurrencyToken()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = BuildOptions(dbName);

        int petId;
        await using (var seedContext = new AppDbContext(options))
        {
            var pet = new Pet
            {
                Type = "Cat",
                UserId = "user-1",
                Stage = PetStage.Egg,
                CreatedAt = DateTime.UtcNow
            };
            seedContext.Pets.Add(pet);
            await seedContext.SaveChangesAsync();
            petId = pet.Id;
        }

        await using var contextA = new AppDbContext(options);
        var petA = await contextA.Pets.FirstAsync(p => p.Id == petId);

        await using (var contextB = new AppDbContext(options))
        {
            var petB = await contextB.Pets.FirstAsync(p => p.Id == petId);
            petB.Xp = 15;
            await contextB.SaveChangesAsync();
        }

        contextA.Entry(petA).Property(p => p.ConcurrencyToken).IsModified = true;
        petA.Nickname = "İstek A tarafından değiştirildi";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextA.SaveChangesAsync());
    }
}