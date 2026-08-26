namespace Dtos;

public class DashboardDto
{
    public int TotalXp { get; set; }

    public List<HabitProgressDto> Habits { get; set; } = new();

    public List<BookDto> Books { get; set; } = new();

    public List<PetDto> Pets { get; set; } = new();

    public FlowerDto? Flower { get; set; }

    public int UnreadNotificationCount { get; set; }

    public int FocusXpPool { get; set; }
}