using Models;

namespace Dtos;

public class BookDto
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Author { get; set; }
    public BookGoalType GoalType { get; set; }
    public int? TotalPages { get; set; }
    public int DailyGoalAmount { get; set; }
    public int CurrentPage { get; set; }
    public int TotalMinutesRead { get; set; }
    public bool IsCompleted { get; set; }

    
    public double? PercentageCompleted { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}