namespace Dtos;

public class BookDailyStatDto
{
    public DateTime Date { get; set; }
    public int TotalAmount { get; set; }
    public bool GoalReached { get; set; }
}