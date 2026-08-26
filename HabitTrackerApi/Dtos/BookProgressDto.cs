// HabitTrackerApi/Dtos/BookProgressDto.cs
namespace Dtos;

public class BookProgressDto
{
    public int BookId { get; set; }

    public int DailyGoalAmount { get; set; }

    public int TodayAmount { get; set; }

    public bool IsGoalReachedToday { get; set; }

    public double PercentageCompletedToday { get; set; }

   
    public int CurrentStreak { get; set; }

    public bool IsCompleted { get; set; }

    public double? OverallPercentageCompleted { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }
}