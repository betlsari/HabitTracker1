namespace Dtos;

public class UserLevelDto
{
    public int TotalXp { get; set; }
    public int Level { get; set; }
    public int CurrentLevelXp { get; set; }
    public int XpForNextLevel { get; set; }
    public double ProgressPercent { get; set; }
}