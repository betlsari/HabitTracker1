using System.ComponentModel.DataAnnotations;

namespace Dtos;

public sealed class UpdateProfileDto
{
    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [MaxLength(2048)]
    [Url]
    public string? AvatarUrl { get; set; }
}