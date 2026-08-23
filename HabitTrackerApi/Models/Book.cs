using System.ComponentModel.DataAnnotations;

namespace Models;

public enum BookGoalType
{
    Pages = 0,
    Minutes = 1
}

public class Book : IHasConcurrencyToken
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }

    public required string Title { get; set; }

    // DÜZELTİLDİ (madde 7): Author için de diğer DTO'larla (Name, Title vb.)
    // tutarlı bir üst sınır eklendi; önceden sınırsız uzunlukta metin DB'ye
    // yazılabiliyordu.
    [MaxLength(200)]
    public string? Author { get; set; }

    public BookGoalType GoalType { get; set; } = BookGoalType.Pages;

    public HabitPeriod Period { get; set; } = HabitPeriod.Daily;

    public int? TotalPages { get; set; }

    public int DailyGoalAmount { get; set; }

    public int CurrentPage { get; set; }

    public int TotalMinutesRead { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public bool ManuallyCompleted { get; set; }
    public bool CompletionBonusAwarded { get; set; }

    // YENİ (madde 6): HabitsController ile aynı desende arşivleme.
    // Arşivlenen kitaplar listelerde varsayılan olarak gizlenir ama
    // ReadingLog geçmişi ve kazanılan XP korunur.
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    // YENİ (madde 7): Kitap için serbest not alanı.
    [MaxLength(1000)]
    public string? Notes { get; set; }

    // YENİ (madde 7): Kapak görseli URL'i. Görsel dosyasının kendisi bu API
    // tarafından barındırılmıyor; istemci harici bir URL (ör. kullanıcının
    // yüklediği bir CDN linki) gönderir.
    [MaxLength(2048)]
    [Url]
    public string? CoverImageUrl { get; set; }

    public List<BookReadingLog> ReadingLogs { get; set; } = new();

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}