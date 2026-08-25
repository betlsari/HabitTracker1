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

public class TwoFactorEmailLoginDto
{
    [Required]
    public required string PreAuthToken { get; set; }

    [Required]
    [StringLength(8, MinimumLength = 6, ErrorMessage = "Email doğrulama kodu geçersiz.")]
    public required string Code { get; set; }
}