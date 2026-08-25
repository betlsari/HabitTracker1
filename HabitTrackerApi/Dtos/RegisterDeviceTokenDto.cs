
using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class RegisterDeviceTokenDto
{
    
    public const int MaxTokenLength = 500;
    public const int MaxPlatformLength = 50;

    [MinLength(8)]
    [MaxLength(MaxTokenLength)]
    public required string Token { get; set; }

    [MinLength(1)]
    [MaxLength(MaxPlatformLength)]
    public string Platform { get; set; } = "unknown";
}

public sealed class UnregisterDeviceDto
{
    [Required]
    [MinLength(8)]
    [MaxLength(RegisterDeviceTokenDto.MaxTokenLength)]
    public required string Token { get; set; }
}