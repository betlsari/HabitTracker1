using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class CreateCompletionDto
{
    public DateTime CompletionDate { get; set; }

    [Range(0, int.MaxValue)]
    public int Amount { get; set; }
}