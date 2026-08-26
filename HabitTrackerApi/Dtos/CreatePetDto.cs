using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class CreatePetDto
{
    [MinLength(1)]
    public required string Type { get; set; }

    [MaxLength(100)]
    public string? Nickname { get; set; }

    
}