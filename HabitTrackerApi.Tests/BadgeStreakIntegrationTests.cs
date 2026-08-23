using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class BadgeStreakIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public BadgeStreakIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Three_consecutive_daily_goal_completions_award_Streak3_badge()
    {
        using var client = _factory.CreateClient();

        var token = await _factory.CreateAccessTokenAsync();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // ---------------------------------------------------------
        // 1. Habit oluştur
        // ---------------------------------------------------------

        var createHabitResponse = await client.PostAsJsonAsync(
            "/api/habits",
            new
            {
                name = $"Streak habit {Guid.NewGuid():N}",
                category = "Spor",
                dailyGoal = 1,
                period = "Daily"
            });

        var createHabitBody =
            await createHabitResponse.Content.ReadAsStringAsync();

        Assert.True(
            createHabitResponse.IsSuccessStatusCode,
            $"Habit oluşturulamadı.\n" +
            $"Status: {createHabitResponse.StatusCode}\n" +
            $"Response: {createHabitBody}"
        );

        var habit =
            await createHabitResponse.Content.ReadFromJsonAsync<HabitResponse>();

        Assert.NotNull(habit);

        // ---------------------------------------------------------
        // 2. Test kullanıcısının DB kaydını ve habit'i al
        // ---------------------------------------------------------

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var dbHabit = await db.Habits
                .SingleAsync(h => h.Id == habit!.Id);

            // Habit'in oluşturulma tarihini 3 gün öncesine çekiyoruz.
            // Böylece geçmiş üç güne completion ekleyebiliriz.
            dbHabit.CreatedAt = DateTime.UtcNow.AddDays(-3);

            await db.SaveChangesAsync();
        }

        // ---------------------------------------------------------
        // 3. Art arda 3 gün completion oluştur
        // ---------------------------------------------------------

        var baseDate = DateTime.UtcNow.Date.AddDays(-2);

        for (var i = 0; i < 3; i++)
        {
            var completionDate = baseDate
                .AddDays(i)
                .AddHours(10);

            var completionResponse = await client.PostAsJsonAsync(
                $"/api/habits/{habit!.Id}/habitcompletions",
                new
                {
                    completionDate,
                    amount = 1
                });

            var responseBody =
                await completionResponse.Content.ReadAsStringAsync();

            Assert.True(
                completionResponse.IsSuccessStatusCode,
                $"Habit completion oluşturulamadı.\n" +
                $"Gün: {i + 1}\n" +
                $"CompletionDate: {completionDate:O}\n" +
                $"Status: {completionResponse.StatusCode}\n" +
                $"Response: {responseBody}"
            );
        }

        // ---------------------------------------------------------
        // 4. Badge'leri kontrol et
        // ---------------------------------------------------------

        var badgesResponse =
            await client.GetAsync("/api/badges");

        var badgesBody =
            await badgesResponse.Content.ReadAsStringAsync();

        Assert.True(
            badgesResponse.IsSuccessStatusCode,
            $"Badge listesi alınamadı.\n" +
            $"Status: {badgesResponse.StatusCode}\n" +
            $"Response: {badgesBody}"
        );

        var badges =
            await badgesResponse.Content
                .ReadFromJsonAsync<List<BadgeResponse>>();

        Assert.NotNull(badges);

        // STREAK_3 kazanılmış olmalı
        var streak3 =
            badges.Single(b => b.Code == "STREAK_3");

        Assert.True(streak3.Earned);

        // 7 günlük seri henüz oluşmadı
        var streak7 =
            badges.Single(b => b.Code == "STREAK_7");

        Assert.False(streak7.Earned);
    }

    private sealed class HabitResponse
    {
        public int Id { get; set; }
    }

    private sealed class BadgeResponse
    {
        public required string Code { get; set; }

        public bool Earned { get; set; }
    }
}