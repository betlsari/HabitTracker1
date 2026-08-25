// HabitTrackerApi.Tests/HistoryLookbackBoundaryTests.cs
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Configuration;
using Models;
using Services;
using Dtos;
using Xunit;

namespace HabitTrackerApi.Tests;

// YENİ: MaxHistoryLookbackDays sınırının hem cutoff öncesi kayıtları
// gerçekten dışladığını hem de HistoryTruncated sinyalinin doğru
// işaretlendiğini doğrulayan regresyon testleri.
public class HistoryLookbackBoundaryTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static IOptions<AppLimitsOptions> Limits(int maxHistoryLookbackDays) =>
        Options.Create(new AppLimitsOptions { MaxHistoryLookbackDays = maxHistoryLookbackDays });

    [Fact]
    public async Task LoadPeriodTotalsAsync_ExcludesCompletionsOlderThanCutoff()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new HabitProgressService(context, new XpService(), Limits(30));

        var habit = new Habit
        {
            Name = "Test",
            Category = HabitCategories.Water,
            DailyGoal = 1,
            UserId = "user-1",
            CreatedAt = DateTime.UtcNow.AddDays(-100)
        };
        context.Habits.Add(habit);
        await context.SaveChangesAsync();

        // Cutoff dışında kalan (30 günden eski) bir completion.
        context.HabitCompletions.Add(new HabitCompletion
        {
            HabitId = habit.Id,
            CompletionDate = DateTime.UtcNow.AddDays(-50),
            Amount = 1
        });

        // Cutoff içinde kalan bir completion.
        context.HabitCompletions.Add(new HabitCompletion
        {
            HabitId = habit.Id,
            CompletionDate = DateTime.UtcNow.AddDays(-5),
            Amount = 1
        });
        await context.SaveChangesAsync();

        var tz = TimeZones.Resolve("Europe/Istanbul");
        var totals = await service.LoadPeriodTotalsAsync(habit.Id, habit.Period, tz);

        // Sadece cutoff içindeki (son 5 gün) completion sayılmalı; toplamda
        // tek bir period'da 1 birim olmalı, 50 gün öncesi hiç görünmemeli.
        Assert.Equal(1, totals.Values.Sum());
    }

    [Fact]
    public async Task GetProgressAsync_HabitOlderThanCutoff_SetsHistoryTruncatedTrue()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new HabitProgressService(context, new XpService(), Limits(30));

        var habit = new Habit
        {
            Name = "Eski alışkanlık",
            Category = HabitCategories.Sport,
            DailyGoal = 1,
            UserId = "user-1",
            // Cutoff (30 gün) öncesinde oluşturulmuş.
            CreatedAt = DateTime.UtcNow.AddDays(-60)
        };
        context.Habits.Add(habit);
        await context.SaveChangesAsync();

        var progress = await service.GetProgressAsync(habit, "Europe/Istanbul");

        Assert.True(progress.HistoryTruncated);
    }

    [Fact]
    public async Task GetProgressAsync_HabitNewerThanCutoff_SetsHistoryTruncatedFalse()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new HabitProgressService(context, new XpService(), Limits(30));

        var habit = new Habit
        {
            Name = "Yeni alışkanlık",
            Category = HabitCategories.Sport,
            DailyGoal = 1,
            UserId = "user-1",
            // Cutoff (30 gün) içinde oluşturulmuş.
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        context.Habits.Add(habit);
        await context.SaveChangesAsync();

        var progress = await service.GetProgressAsync(habit, "Europe/Istanbul");

        Assert.False(progress.HistoryTruncated);
    }

    [Fact]
    public async Task GetComparisonAsync_HabitOlderThanEffectiveCutoff_SetsHistoryTruncatedTrue()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        // MaxHistoryLookbackDays çok yüksek (730) ama lookbackPeriods=5
        // istendiği için etkin cutoff çok daha dar olmalı ve habit bunun
        // dışında kalmalı.
        var service = new HabitProgressService(context, new XpService(), Limits(730));

        var habit = new Habit
        {
            Name = "Uzun süredir var olan alışkanlık",
            Category = HabitCategories.Sport,
            DailyGoal = 1,
            UserId = "user-1",
            CreatedAt = DateTime.UtcNow.AddDays(-100)
        };
        context.Habits.Add(habit);
        await context.SaveChangesAsync();

        var result = await service.GetComparisonAsync(new[] { habit }, "Europe/Istanbul", lookbackPeriods: 5);

        Assert.True(result.Single().HistoryTruncated);
    }

    [Fact]
    public async Task BookService_LoadPeriodTotalsAsync_ExcludesLogsOlderThanCutoff()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context, Limits(30));

        var book = new Book
        {
            Title = "Test Book",
            UserId = "user-1",
            DailyGoalAmount = 10,
            TotalPages = 200,
            CreatedAt = DateTime.UtcNow.AddDays(-100)
        };
        context.Books.Add(book);
        await context.SaveChangesAsync();

        context.BookReadingLogs.Add(new BookReadingLog
        {
            BookId = book.Id,
            ReadDate = DateTime.UtcNow.AddDays(-50), // cutoff dışı
            Amount = 10
        });
        context.BookReadingLogs.Add(new BookReadingLog
        {
            BookId = book.Id,
            ReadDate = DateTime.UtcNow.AddDays(-5), // cutoff içi
            Amount = 10
        });
        await context.SaveChangesAsync();

        var tz = TimeZones.Resolve("Europe/Istanbul");
        var totals = await service.LoadPeriodTotalsAsync(book.Id, book.Period, tz);

        Assert.Equal(10, totals.Values.Sum());
    }

    [Fact]
    public async Task BookService_GetProgressAsync_BookOlderThanCutoff_SetsHistoryTruncatedTrue()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString("N"));
        var service = new BookService(context, Limits(30));

        var book = new Book
        {
            Title = "Eski kitap",
            UserId = "user-1",
            DailyGoalAmount = 10,
            TotalPages = 200,
            CreatedAt = DateTime.UtcNow.AddDays(-60)
        };
        context.Books.Add(book);
        await context.SaveChangesAsync();

        var progress = await service.GetProgressAsync(book, "Europe/Istanbul");

        Assert.True(progress.HistoryTruncated);
    }
}