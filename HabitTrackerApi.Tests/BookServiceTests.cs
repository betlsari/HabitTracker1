using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using Dtos;
using Xunit;

namespace HabitTrackerApi.Tests;

public class BookServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static Book CreateBook(string userId = "user-1") => new()
    {
        Title = "Test Book",
        UserId = userId,
        GoalType = BookGoalType.Pages,
        DailyGoalAmount = 10,
        TotalPages = 100,
        Period = HabitPeriod.Daily,
        CreatedAt = DateTime.UtcNow.AddDays(-5)
    };

    [Fact]
    public async Task AddReadingLogAsync_AwardsBaseXpAndTracksPage()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context);

        var book = CreateBook();
        context.Books.Add(book);
        await context.SaveChangesAsync();

        var dto = new LogReadingDto { ReadDate = DateTime.UtcNow, Amount = 5 };
        var result = await service.AddReadingLogAsync(book, dto, timeZoneId: "Europe/Istanbul");

        Assert.Equal(3, result.XpEarned); // sadece XpPerLog, hedef henüz tutturulmadı
        Assert.Equal(5, book.CurrentPage);
        Assert.False(result.GoalJustReachedInPeriod);
    }

    [Fact]
    public async Task AddReadingLogAsync_GoalReached_AddsBonusXp()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context);

        var book = CreateBook();
        context.Books.Add(book);
        await context.SaveChangesAsync();

        var dto = new LogReadingDto { ReadDate = DateTime.UtcNow, Amount = 10 };
        var result = await service.AddReadingLogAsync(book, dto, timeZoneId: "Europe/Istanbul");

        Assert.True(result.GoalJustReachedInPeriod);
        Assert.Equal(3 + 5, result.XpEarned); // XpPerLog + DailyGoalBonusXp
    }

    [Fact]
    public async Task AddReadingLogAsync_ReachingTotalPages_CompletesBookWithBonus()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context);

        var book = CreateBook();
        book.TotalPages = 10;
        context.Books.Add(book);
        await context.SaveChangesAsync();

        var dto = new LogReadingDto { ReadDate = DateTime.UtcNow, Amount = 10 };
        var result = await service.AddReadingLogAsync(book, dto, timeZoneId: "Europe/Istanbul");

        Assert.True(result.BookJustCompleted);
        Assert.True(book.IsCompleted);
        Assert.Equal(3 + 5 + BookService.CompletionBonusXp, result.XpEarned);
    }

    [Fact]
    public async Task RecalculateBookAsync_ReturnsXpDeltaAfterGoalChange()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context);

        var book = CreateBook();
        context.Books.Add(book);
        await context.SaveChangesAsync();

        await service.AddReadingLogAsync(book, new LogReadingDto { ReadDate = DateTime.UtcNow, Amount = 5 }, "Europe/Istanbul");

        // Günlük hedefi düşürerek geçmiş logun artık hedefi tutturmuş olmasını sağlıyoruz.
        book.DailyGoalAmount = 5;
        var delta = await service.RecalculateBookAsync(book, "Europe/Istanbul");

        Assert.Equal(5, delta); // DailyGoalBonusXp geriye dönük olarak eklendi
    }

    [Fact]
    public async Task GetComparisonAsync_RanksBooksByCompletionRate()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context);

        var strongBook = CreateBook();
        strongBook.Title = "Strong";
        var weakBook = CreateBook();
        weakBook.Title = "Weak";
        context.Books.AddRange(strongBook, weakBook);
        await context.SaveChangesAsync();

        await service.AddReadingLogAsync(strongBook, new LogReadingDto { ReadDate = DateTime.UtcNow, Amount = 10 }, "Europe/Istanbul");
        await service.AddReadingLogAsync(weakBook, new LogReadingDto { ReadDate = DateTime.UtcNow, Amount = 1 }, "Europe/Istanbul");

        var comparison = await service.GetComparisonAsync(new[] { strongBook, weakBook }, "Europe/Istanbul", lookbackDays: 30);

        Assert.Equal("Strong", comparison[0].Title);
        Assert.Equal(1, comparison[0].Rank);
    }
}