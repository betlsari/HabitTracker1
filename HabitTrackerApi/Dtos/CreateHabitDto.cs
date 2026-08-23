// HabitTrackerApi/Dtos/CreateHabitDto.cs
using System.ComponentModel.DataAnnotations;
using Models;

namespace Dtos;

public class CreateHabitDto
{
    public const int MaxDailyGoal = 100_000;
    public const int MaxNotesLength = 1000;

    // YENİ: Name için üst sınır yoktu; istemci keyfi büyüklükte string
    // gönderebiliyordu. Book.Author (200) / Notes (1000) ile tutarlı bir
    // üst sınır eklendi.
    public const int MaxNameLength = 200;

    [MinLength(1)]
    [MaxLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Range(1, MaxDailyGoal)]
    public int DailyGoal { get; set; }

    [MinLength(1)]
    public string Category { get; set; } = string.Empty;

    public HabitPeriod Period { get; set; } = HabitPeriod.Daily;

    public TimeOnly? TargetTime { get; set; }

    public TimeOnly? ReminderTime { get; set; }

    [MaxLength(MaxNotesLength)]
    public string? Notes { get; set; }
}