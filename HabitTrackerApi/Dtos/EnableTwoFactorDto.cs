using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class EnableTwoFactorDto
{
    [Required]
    [StringLength(8, MinimumLength = 6, ErrorMessage = "Doğrulama kodu geçersiz.")]
    public required string Code { get; set; }
}