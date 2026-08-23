namespace Dtos;

public class PetAccessoryDto
{
    public required string Code { get; set; }
    public bool Unlocked { get; set; }
    public bool Equipped { get; set; }
}