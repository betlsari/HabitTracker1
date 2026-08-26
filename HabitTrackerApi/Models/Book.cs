using System.ComponentModel.DataAnnotations;

namespace Models;

public enum BookGoalType
{
    Pages = 0,
    Minutes = 1
}

public class Book 
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }

    public required string Title { get; set; }

    public string NormalizedTitle { get; set; } = string.Empty;

 
    [MaxLength(200)]
    public string? Author { get; set; }

    public BookGoalType GoalType { get; set; } = BookGoalType.Pages;

    public HabitPeriod Period { get; set; } = HabitPeriod.Daily;

    public int? TotalPages { get; set; }

    public int DailyGoalAmount { get; set; }

    public int CurrentPage { get; set; }

    public int TotalMinutesRead { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public bool ManuallyCompleted { get; set; }
    public bool CompletionBonusAwarded { get; set; }

    
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    
    [MaxLength(1000)]
    public string? Notes { get; set; }

    
    [MaxLength(2048)]
    [Url]
    public string? CoverImageUrl { get; set; }

    public List<BookReadingLog> ReadingLogs { get; set; } = new();

    
}