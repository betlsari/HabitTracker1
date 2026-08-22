using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class UpdateTimezoneDto
{
    [MinLength(1)]
    public required string TimeZoneId { get; set; }
}
