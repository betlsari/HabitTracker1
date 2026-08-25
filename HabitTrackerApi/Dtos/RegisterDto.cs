
using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class RegisterDto
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Required]
    [StrongPassword]
    public required string Password { get; set; }

    [Required]
    [Compare(nameof(Password), ErrorMessage = "Şifreler eşleşmiyor.")]
    public required string ConfirmPassword { get; set; }

    public string? CaptchaToken { get; set; }
}