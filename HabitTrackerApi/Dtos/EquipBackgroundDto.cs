using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class EquipBackgroundDto
{
    [Required]
    public required string Background { get; set; }
}