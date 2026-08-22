using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class UpdatePetDto
{
    [MaxLength(50)]
    public string? Nickname { get; set; }
}
