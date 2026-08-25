
using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class ResetPasswordDto
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Required]
    public required string Token { get; set; }

    
    [Required]
    [StrongPassword]
    public required string NewPassword { get; set; }

    public string? CaptchaToken { get; set; }
}