namespace Dtos;

public class MonthlyStatDto
{
    public DateTime Month { get; set; }
    public int HabitCompletions { get; set; }
    public int BookLogEntries { get; set; }
    public int TotalXpEarned { get; set; }
}

public class MonthlySummaryDto
{
    public required List<MonthlyStatDto> Months { get; set; }
    public MonthlyStatDto? BestMonth { get; set; }
    public int CurrentMonthXp { get; set; }
    public int TotalXpAllTime { get; set; }
}