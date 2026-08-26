
using System.ComponentModel.DataAnnotations;
using Dtos;

namespace Models;

public class DeviceToken 
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }

   
    [MaxLength(RegisterDeviceTokenDto.MaxTokenLength)]
    public required string Token { get; set; }

    [MaxLength(RegisterDeviceTokenDto.MaxPlatformLength)]
    public string Platform { get; set; } = "unknown";

    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }

   
}