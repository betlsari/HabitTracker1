
using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class ChangePasswordDto
{
    [Required]
    public required string CurrentPassword { get; set; }

    
    [Required]
    [StrongPassword]
    public required string NewPassword { get; set; }
}