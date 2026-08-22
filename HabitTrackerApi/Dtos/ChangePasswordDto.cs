using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class ChangePasswordDto
{
    [Required]
    public required string CurrentPassword { get; set; }

    [Required]
    [MinLength(6, ErrorMessage = "Yeni şifre en az 6 karakter olmalıdır.")]
    public required string NewPassword { get; set; }
}
