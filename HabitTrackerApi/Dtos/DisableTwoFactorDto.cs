using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class DisableTwoFactorDto
{
    [Required]
    public required string CurrentPassword { get; set; }
}