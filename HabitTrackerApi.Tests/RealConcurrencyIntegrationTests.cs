using System.Net;
using System.Net.Http.Json;
using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;

namespace HabitTrackerApi.Tests;

[Trait("Category", "RequiresDocker")]
public sealed class RealConcurrencyIntegrationTests : IClassFixture<PostgresApiFactory>
{
    private readonly PostgresApiFactory _factory;
    public RealConcurrencyIntegrationTests(PostgresApiFactory factory) => _factory = factory;

    // Bu test, PetsController.FeedPet üzerindeki
    // "pg_advisory_xact_lock(hashtext('pet:' + userId))" korumasının
    // GERÇEKTEN çalıştığını doğrular. Kilit olmasaydı: N paralel istek
    // hepsi aynı anda "TotalXp >= feedCostXp" kontrolünü geçer (klasik
    // check-then-act race), her biri XP düşer ve kullanıcının XP'si
    // negatife düşebilirdi.
    [Fact]
    public async Task Concurrent_feed_requests_never_allow_more_successes_than_affordable_and_never_go_negative()
    {
        var token = await _factory.CreateAccessTokenAsync();
        using var setupClient = _factory.CreateAuthenticatedClient(token);

        // Kullanıcıya tam olarak feedCostXp * ALLOWED_FEEDS kadar XP ver —
        // varsayılan AppLimits.PetFeedCostXp = 3, dolayısıyla 3 XP ile tam
        // olarak 1 başarılı besleme yapılabilmeli.
        const int allowedFeeds = 1;
        int feedCostXp;
        int petId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

            // Test kullanıcısının Id'sini JWT'den bağımsız, DB üzerinden bul.
            var createPetResponse = await setupClient.PostAsJsonAsync("/api/pets", new { type = "Cat" });
            createPetResponse.EnsureSuccessStatusCode();
            var pet = await createPetResponse.Content.ReadFromJsonAsync<PetIdResponse>();
            petId = pet!.Id;

            var petEntity = await db.Pets.AsNoTracking().FirstAsync(p => p.Id == petId);
            var user = await userManager.FindByIdAsync(petEntity.UserId);
            Assert.NotNull(user);

            feedCostXp = 3; // appsettings default: AppLimits:PetFeedCostXp
            user!.TotalXp = feedCostXp * allowedFeeds;
            await userManager.UpdateAsync(user);
        }

        const int parallelRequests = 10;
        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < parallelRequests; i++)
        {
            var client = _factory.CreateAuthenticatedClient(token);
            tasks.Add(client.PostAsync($"/api/pets/{petId}/feed", null));
        }

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var badRequestCount = responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest);

        // Advisory lock çalışıyorsa: yalnızca kullanıcının XP'sinin
        // karşılayabileceği kadar istek (burada tam olarak 1) başarılı
        // olmalı, geri kalanı "yetersiz XP" ile reddedilmeli.
        Assert.Equal(allowedFeeds, successCount);
        Assert.Equal(parallelRequests - allowedFeeds, badRequestCount);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var petAfter = await verifyDb.Pets.AsNoTracking().FirstAsync(p => p.Id == petId);
        var userAfter = await verifyScope.ServiceProvider.GetRequiredService<UserManager<User>>()
            .FindByIdAsync(petAfter.UserId);

        // Kilit olmasaydı burada TotalXp negatife düşebilirdi (birden fazla
        // istek aynı anda "yeterli XP var" görüp düşürseydi).
        Assert.True(userAfter!.TotalXp >= 0, $"TotalXp negatife düştü: {userAfter.TotalXp}");
        Assert.Equal(0, userAfter.TotalXp); // tam olarak 1 kez düşülmüş olmalı
    }

    // Aynı desenin CreatePet (yumurta maliyeti) üzerinde de doğrulanması:
    // N paralel "yeni pet oluştur" isteği, kullanıcının MaxPetsPerUser
    // limitini asla aşmamalı.
    [Fact]
    public async Task Concurrent_create_pet_requests_never_exceed_max_pets_per_user()
    {
        var token = await _factory.CreateAccessTokenAsync();

        // İlk pet'i serbestçe oluştur (ücretsiz), ardından kullanıcıya
        // sınırsız XP ver ki sıradaki tüm istekler sadece "limit" kontrolüyle
        // sınırlansın.
        using (var setupClient = _factory.CreateAuthenticatedClient(token))
        {
            var first = await setupClient.PostAsJsonAsync("/api/pets", new { type = "Cat" });
            first.EnsureSuccessStatusCode();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var pet = await db.Pets.AsNoTracking().FirstAsync();
            var user = await userManager.FindByIdAsync(pet.UserId);
            user!.TotalXp = 100_000;
            await userManager.UpdateAsync(user);
        }

        // AppLimits:MaxPetsPerUser varsayılanı 5 — zaten 1 tane var, kalan
        // kapasite 4. 10 paralel istekle bunu aşmaya çalış.
        const int parallelRequests = 10;
        var tasks = Enumerable.Range(0, parallelRequests)
            .Select(_ => _factory.CreateAuthenticatedClient(token).PostAsJsonAsync("/api/pets", new { type = "Dog" }))
            .ToList();

        var responses = await Task.WhenAll(tasks);
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var totalPetsForUser = await verifyDb.Pets.CountAsync();

        Assert.True(totalPetsForUser <= 5, $"MaxPetsPerUser aşıldı: {totalPetsForUser} pet oluşturuldu.");
        Assert.Equal(totalPetsForUser, 1 + successCount);
    }

    private sealed class PetIdResponse
    {
        public int Id { get; set; }
    }
}