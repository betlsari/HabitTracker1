using System.ComponentModel.DataAnnotations;

namespace Dtos;


public class RegenerateRecoveryCodesDto
{
    [Required]
    public required string CurrentPassword { get; set; }
}