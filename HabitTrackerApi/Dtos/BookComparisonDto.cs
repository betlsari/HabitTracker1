namespace Dtos;

public class BookComparisonDto
{
    public int BookId { get; set; }
    public required string Title { get; set; }

    public int CurrentStreak { get; set; }

    public double CompletionRatePercent { get; set; }

    public int Rank { get; set; }

    
    public bool HistoryTruncated { get; set; }
}