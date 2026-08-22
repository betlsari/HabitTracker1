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
    public string? Author { get; set; }

    public BookGoalType GoalType { get; set; } = BookGoalType.Pages;

    // YENİ: Doküman "ay veya hafta için belirlediği okuma hedefini ekleyebilir"
    // diyor; önceden DailyGoalAmount her zaman "gün" bazında yorumlanıyordu.
    // Artık Habit'teki Period (Daily/Weekly/Monthly) ile aynı enum kullanılarak
    // DailyGoalAmount'ın hangi dönem için geçerli olduğu ayrıca belirtilebiliyor.
    public HabitPeriod Period { get; set; } = HabitPeriod.Daily;

    // GoalType = Pages ise kitabın toplam sayfa sayısı (ilerleme yüzdesi için opsiyonel)
    public int? TotalPages { get; set; }

    // Dönemsel (Period'a göre) hedef miktarı (sayfa ya da dakika, GoalType'a göre yorumlanır)
    public int DailyGoalAmount { get; set; }

    public int CurrentPage { get; set; }

    public int TotalMinutesRead { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    
    public bool ManuallyCompleted { get; set; }
    public bool CompletionBonusAwarded { get; set; }

    public List<BookReadingLog> ReadingLogs { get; set; } = new();

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
