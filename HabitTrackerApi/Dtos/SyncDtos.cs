using System.ComponentModel.DataAnnotations;

namespace Dtos;


public class BatchSyncRequestDto
{
    public const int MaxItems = 200;

    [MinLength(0)]
    public List<BatchHabitCompletionItemDto> HabitCompletions { get; set; } = new();

    public List<BatchBookReadingLogItemDto> BookReadingLogs { get; set; } = new();
}

public class BatchHabitCompletionItemDto
{
    [Required]
    public required string ClientRequestId { get; set; }

    [Required]
    public int HabitId { get; set; }

    [Required]
    public DateTime CompletionDate { get; set; }

    [Range(0, CreateCompletionDto.MaxAmount)]
    public int Amount { get; set; }
}

public class BatchBookReadingLogItemDto
{
    [Required]
    public required string ClientRequestId { get; set; }

    [Required]
    public int BookId { get; set; }

    [Required]
    public DateTime ReadDate { get; set; }

    [Range(0, LogReadingDto.MaxAmount)]
    public int Amount { get; set; }

    [Range(0, LogReadingDto.MaxPageReached)]
    public int? PageReachedAt { get; set; }
}

public class BatchSyncResultDto
{
    public List<BatchItemResultDto> HabitCompletions { get; set; } = new();
    public List<BatchItemResultDto> BookReadingLogs { get; set; } = new();
}

public class BatchItemResultDto
{
    public required string ClientRequestId { get; set; }
    public bool Success { get; set; }
    public int? CreatedId { get; set; }
    public string? Error { get; set; }
}