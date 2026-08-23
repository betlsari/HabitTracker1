using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Xunit;

namespace HabitTrackerApi.Tests;


public class BookConcurrencyTests
{
    private static DbContextOptions<AppDbContext> BuildOptions(string dbName) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

    [Fact]
    public async Task Concurrent_book_edits_are_detected_via_ConcurrencyToken()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = BuildOptions(dbName);

        int bookId;
        await using (var seedContext = new AppDbContext(options))
        {
            var book = new Book
            {
                Title = "Concurrency Book",
                UserId = "user-1",
                DailyGoalAmount = 10,
                TotalPages = 100,
                CreatedAt = DateTime.UtcNow
            };
            seedContext.Books.Add(book);
            await seedContext.SaveChangesAsync();
            bookId = book.Id;
        }

       
        await using var contextA = new AppDbContext(options);
        var bookA = await contextA.Books.FirstAsync(b => b.Id == bookId);

        
        await using (var contextB = new AppDbContext(options))
        {
            var bookB = await contextB.Books.FirstAsync(b => b.Id == bookId);
            bookB.DailyGoalAmount = 20;
            await contextB.SaveChangesAsync();
        }

       
        contextA.Entry(bookA).Property(b => b.ConcurrencyToken).IsModified = true;
        bookA.Notes = "İstek A tarafından değiştirildi";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextA.SaveChangesAsync());
    }
}