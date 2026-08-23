namespace Models;

public class BookReadingLog : IHasConcurrencyToken
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    public DateTime ReadDate { get; set; }

    public int Amount { get; set; }

    public int? PageReachedAt { get; set; }

    public int XpEarned { get; set; }

    
    public string? ClientRequestId { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}