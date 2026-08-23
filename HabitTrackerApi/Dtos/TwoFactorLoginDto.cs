using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class TwoFactorLoginDto
{
    [Required]
    public required string PreAuthToken { get; set; }

    
    [Required]
    [StringLength(64, MinimumLength = 6, ErrorMessage = "Doğrulama kodu geçersiz.")]
    public required string Code { get; set; }
}