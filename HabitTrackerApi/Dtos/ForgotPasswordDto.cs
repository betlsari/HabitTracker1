using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class ForgotPasswordDto
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }
}