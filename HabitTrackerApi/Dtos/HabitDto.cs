namespace Dtos;

public class HabitDto
{
    public int Id { get; set; }

    public string Name { get; set; }
    public string Category{ get; set; }


    public int DailyGoal { get; set; }

    public DateTime CreatedAt { get; set; }
}