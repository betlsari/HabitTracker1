using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class GrowPetFromFocusDto
{
    [Range(1, 100_000)]
    public int Amount { get; set; }
}