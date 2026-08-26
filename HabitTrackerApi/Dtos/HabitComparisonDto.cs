namespace Dtos;

public class HabitComparisonDto
{
    public int HabitId { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }

    public int CurrentStreak { get; set; }

    public double CompletionRatePercent { get; set; }

    public double PercentageCompletedThisPeriod { get; set; }

    public int Rank { get; set; }
}