using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Models;
using Services;
using Dtos;
using Xunit;

namespace HabitTrackerApi.Tests;

public class BookServiceTests : IDisposable
{
    private readonly List<SqliteConnection> _connections = new();

    private AppDbContext CreateContext(string dbName)
    {
        var connection = new SqliteConnection($"DataSource=file:{dbName}?mode=memory&cache=shared");
        connection.Open();
        
        // PostgreSQL fonksiyonunu SQLite'a öğretiyoruz
        connection.CreateFunction<string, int>("hashtext", text => text != null ? text.GetHashCode() : 0);

        _connections.Add(connection);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.Close();
            connection.Dispose();
        }
    }

    private static Book CreateBook(string userId) => new()
    {
        Title = "Test Book",
        UserId = userId,
        GoalType = BookGoalType.Pages,
        DailyGoalAmount = 10,
        TotalPages = 100,
        Period = HabitPeriod.Daily,
        CreatedAt = DateTime.UtcNow.AddDays(-5)
    };

    // Yardımcı metod: Book eklemeden önce User eklemek için
    private async Task<User> CreateTestUserAsync(AppDbContext context, string userId = "user-1")
    {
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task AddReadingLogAsync_AwardsBaseXpAndTracksPage()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context);

        var user = await CreateTestUserAsync(context);
        var book = CreateBook(user.Id);
        
        context.Books.Add(book);
        await context.SaveChangesAsync();

        var dto = new LogReadingDto { ReadDate = DateTime.UtcNow, Amount = 5 };
        var result = await service.AddReadingLogAsync(book, dto, timeZoneId: "Europe/Istanbul");

        Assert.Equal(3, result.XpEarned);
        Assert.Equal(5, book.CurrentPage);
        Assert.False(result.GoalJustReachedInPeriod);
    }

    [Fact]
    public async Task AddReadingLogAsync_GoalReached_AddsBonusXp()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context);

        var user = await CreateTestUserAsync(context);
        var book = CreateBook(user.Id);
        context.Books.Add(book);
        await context.SaveChangesAsync();

        var dto = new LogReadingDto { ReadDate = DateTime.UtcNow, Amount = 10 };
        var result = await service.AddReadingLogAsync(book, dto, timeZoneId: "Europe/Istanbul");

        Assert.True(result.GoalJustReachedInPeriod);
        Assert.Equal(3 + 5, result.XpEarned);
    }

    [Fact]
    public async Task AddReadingLogAsync_ReachingTotalPages_CompletesBookWithBonus()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context);

        var user = await CreateTestUserAsync(context);
        var book = CreateBook(user.Id);
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

        var user = await CreateTestUserAsync(context);
        var book = CreateBook(user.Id);
        context.Books.Add(book);
        await context.SaveChangesAsync();

        await service.AddReadingLogAsync(book, new LogReadingDto { ReadDate = DateTime.UtcNow, Amount = 5 }, "Europe/Istanbul");

        book.DailyGoalAmount = 5;
        var delta = await service.RecalculateBookAsync(book, "Europe/Istanbul");

        Assert.Equal(5, delta);
    }

    [Fact]
    public async Task GetComparisonAsync_RanksBooksByCompletionRate()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context);

        var user = await CreateTestUserAsync(context);
        
        var strongBook = CreateBook(user.Id);
        strongBook.Title = "Strong";
        
        var weakBook = CreateBook(user.Id);
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