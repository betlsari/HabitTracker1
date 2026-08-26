using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class UpdatePetDto
{
    [MaxLength(100)]
    public string? Nickname { get; set; }
}