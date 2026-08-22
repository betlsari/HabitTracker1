namespace Models;

public class BookReadingLog
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    public DateTime ReadDate { get; set; }

    public int Amount { get; set; }

    public int? PageReachedAt { get; set; }

    
    public int XpEarned { get; set; }
}