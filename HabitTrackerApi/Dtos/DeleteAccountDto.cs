using System.ComponentModel.DataAnnotations;

namespace Dtos;


public class DeleteAccountDto
{
    [Required]
    public required string CurrentPassword { get; set; }

    [StringLength(64, MinimumLength = 6)]
    public string? TwoFactorCode { get; set; }
}