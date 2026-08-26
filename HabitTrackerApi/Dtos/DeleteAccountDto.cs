using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class DeleteAccountDto
{
    [Required]
    public required string CurrentPassword { get; set; }
}