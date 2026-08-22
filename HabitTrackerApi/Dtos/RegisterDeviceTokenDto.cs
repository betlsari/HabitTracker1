using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class RegisterDeviceTokenDto
{
    [MinLength(8)]
    public required string Token { get; set; }

    [MinLength(1)]
    public string Platform { get; set; } = "unknown";
}
