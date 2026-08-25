using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class LoginDto
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Required]
    public required string Password { get; set; }

    public string? CaptchaToken { get; set; }
}
