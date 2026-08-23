namespace Dtos;


public class DeviceTokenDto
{
    public int Id { get; set; }
    public required string Platform { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public required string TokenSuffix { get; set; }
}