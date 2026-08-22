using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class ResendConfirmationDto
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }
}
