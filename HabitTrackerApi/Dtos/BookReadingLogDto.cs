namespace Dtos;

public class BookReadingLogDto
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public DateTime ReadDate { get; set; }
    public int Amount { get; set; }
    public int? PageReachedAt { get; set; }
    public int XpEarned { get; set; }
}