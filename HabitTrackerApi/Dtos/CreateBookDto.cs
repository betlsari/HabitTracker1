using System.ComponentModel.DataAnnotations;
using Models;

namespace Dtos;

public class CreateBookDto
{
    [MinLength(1)]
    public string Title { get; set; } = string.Empty;

    public string? Author { get; set; }

    public BookGoalType GoalType { get; set; } = BookGoalType.Pages;

    [Range(1, int.MaxValue)]
    public int DailyGoalAmount { get; set; }

    [Range(1, int.MaxValue)]
    public int? TotalPages { get; set; }
}