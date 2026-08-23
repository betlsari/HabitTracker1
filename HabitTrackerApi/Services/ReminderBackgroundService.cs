using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

// DÜZELTİLDİ (🟡 N+1 / performans): Önceden habit ve book toplamları
// (LoadPeriodTotalsAsync) her habit/book için AYRI AYRI çağrılıyordu — bu
// job her dakika çalıştığı için kullanıcı sayısı arttıkça DB'ye ciddi yük
// biniyordu. Artık habit'ler/kitaplar (Period, TimeZoneId) kombinasyonuna
// göre gruplanıp her grup için TEK toplu sorgu (LoadPeriodTotalsBatchAsync)
// atılıyor. Ayrıca aynı totals haritası hem "hatırlatma" hem de "kaçırıldı"
// geçişinde tekrar kullanılıyor — önceden bu iki geçiş birbirinden habersiz,
// aynı veriyi iki kez sorguluyordu.
public class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReminderBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int MinuteTolerance = 1;

    public ReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var progress = scope.ServiceProvider.GetRequiredService<HabitProgressService>();
                var bookService = scope.ServiceProvider.GetRequiredService<BookService>();
                var missedHour = _configuration.GetValue("Notifications:MissedHabitLocalHour", 21);

                await RunHabitPassAsync(db, notifications, progress, missedHour, stoppingToken);
                await RunBookPassAsync(db, notifications, bookService, missedHour, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hatırlatma job'ı sırasında hata oluştu.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }

    private static async Task RunHabitPassAsync(
        AppDbContext db,
        NotificationService notifications,
        HabitProgressService progress,
        int missedHour,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var habits = await db.Habits.AsNoTracking()
            .Include(h => h.User)
            .Where(h => !h.IsArchived)
            .ToListAsync(cancellationToken);

        if (habits.Count == 0)
        {
            return;
        }

        var tzByHabit = habits.ToDictionary(h => h.Id, h => TimeZones.Resolve(h.User?.TimeZoneId));
        var totalsByHabit = await LoadHabitTotalsGroupedAsync(progress, habits, tzByHabit, cancellationToken);

        await SendRemindersAsync(habits, tzByHabit, totalsByHabit, notifications, now, cancellationToken);
        await SendMissedAsync(habits, tzByHabit, totalsByHabit, notifications, missedHour, now, cancellationToken);
    }

    private static async Task<Dictionary<int, Dictionary<DateTime, int>>> LoadHabitTotalsGroupedAsync(
        HabitProgressService progress,
        List<Habit> habits,
        Dictionary<int, TimeZoneInfo> tzByHabit,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, Dictionary<DateTime, int>>();

        foreach (var group in habits.GroupBy(h => (h.Period, TzId: tzByHabit[h.Id].Id)))
        {
            var habitIds = group.Select(h => h.Id).ToArray();
            var tz = tzByHabit[group.First().Id];
            var batch = await progress.LoadPeriodTotalsBatchAsync(habitIds, group.Key.Period, tz, cancellationToken);
            foreach (var kv in batch)
            {
                result[kv.Key] = kv.Value;
            }
        }

        return result;
    }

    private static async Task SendRemindersAsync(
        List<Habit> habits,
        Dictionary<int, TimeZoneInfo> tzByHabit,
        Dictionary<int, Dictionary<DateTime, int>> totalsByHabit,
        NotificationService notifications,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var habit in habits)
        {
            if (habit.ReminderTime == null)
            {
                continue;
            }

            var tz = tzByHabit[habit.Id];
            var local = TimeZones.ToLocal(now, tz);
            var reminderTime = habit.ReminderTime.Value.ToTimeSpan();
            if (Math.Abs((local.TimeOfDay - reminderTime).TotalMinutes) > MinuteTolerance)
            {
                continue;
            }

            var totals = totalsByHabit.TryGetValue(habit.Id, out var t) ? t : new Dictionary<DateTime, int>();
            var periodStart = HabitSchedule.PeriodStartLocal(now, habit.Period, tz);
            if (HabitProgressService.IsGoalReached(habit, totals, periodStart))
            {
                continue;
            }

            var localDate = local.Date.ToString("yyyy-MM-dd");
            await notifications.TryEnqueueAsync(
                habit.UserId,
                NotificationTypes.Reminder,
                "Alışkanlık hatırlatması",
                $"{habit.Name} için belirlediğin saat geldi.",
                habit.Id,
                $"reminder:{habit.Id}:{localDate}",
                cancellationToken);
        }
    }

    private static async Task SendMissedAsync(
        List<Habit> habits,
        Dictionary<int, TimeZoneInfo> tzByHabit,
        Dictionary<int, Dictionary<DateTime, int>> totalsByHabit,
        NotificationService notifications,
        int missedHour,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var habit in habits)
        {
            var tz = tzByHabit[habit.Id];
            if (!HabitSchedule.IsEndOfPeriodWindow(now, habit.Period, tz, missedHour, MinuteTolerance))
            {
                continue;
            }

            var totals = totalsByHabit.TryGetValue(habit.Id, out var t) ? t : new Dictionary<DateTime, int>();
            var periodStart = HabitSchedule.PeriodStartLocal(now, habit.Period, tz);
            if (HabitProgressService.IsGoalReached(habit, totals, periodStart))
            {
                continue;
            }

            var keyDate = periodStart.ToString("yyyy-MM-dd");
            var periodLabel = habit.Period switch
            {
                HabitPeriod.Weekly => "haftalık",
                HabitPeriod.Monthly => "aylık",
                _ => "günlük"
            };

            var previousPeriodStart = HabitSchedule.PreviousPeriodStartLocal(periodStart, habit.Period);
            var lostStreak = HabitProgressService.CountStreak(habit, totals, previousPeriodStart, tz);

            if (lostStreak > 0)
            {
                await notifications.TryEnqueueAsync(
                    habit.UserId,
                    NotificationTypes.StreakBroken,
                    "Serin bozuldu",
                    $"{habit.Name} alışkanlığındaki {lostStreak} {periodLabel} zincirin bozuldu. Bugün yeniden başlayabilirsin.",
                    habit.Id,
                    $"streakbroken:{habit.Id}:{keyDate}",
                    cancellationToken);
            }
            else
            {
                await notifications.TryEnqueueAsync(
                    habit.UserId,
                    NotificationTypes.Missed,
                    "Kaçırılan alışkanlık",
                    $"{habit.Name} bu dönem tamamlanmadı. Yarın zinciri yeniden kurabilirsin.",
                    habit.Id,
                    $"missed:{habit.Id}:{keyDate}",
                    cancellationToken);
            }
        }
    }

    private static async Task RunBookPassAsync(
        AppDbContext db,
        NotificationService notifications,
        BookService bookService,
        int missedHour,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var books = await db.Books.AsNoTracking()
            .Include(b => b.User)
            .Where(b => !b.IsCompleted && !b.IsArchived && b.DailyGoalAmount > 0)
            .ToListAsync(cancellationToken);

        if (books.Count == 0)
        {
            return;
        }

        var tzByBook = books.ToDictionary(b => b.Id, b => TimeZones.Resolve(b.User?.TimeZoneId));
        var totalsByBook = new Dictionary<int, Dictionary<DateTime, int>>();

        foreach (var group in books.GroupBy(b => (b.Period, TzId: tzByBook[b.Id].Id)))
        {
            var bookIds = group.Select(b => b.Id).ToArray();
            var tz = tzByBook[group.First().Id];
            var batch = await bookService.LoadPeriodTotalsBatchAsync(bookIds, group.Key.Period, tz, cancellationToken);
            foreach (var kv in batch)
            {
                totalsByBook[kv.Key] = kv.Value;
            }
        }

        foreach (var book in books)
        {
            var tz = tzByBook[book.Id];
            if (!HabitSchedule.IsEndOfPeriodWindow(now, book.Period, tz, missedHour, MinuteTolerance))
            {
                continue;
            }

            var totals = totalsByBook.TryGetValue(book.Id, out var t) ? t : new Dictionary<DateTime, int>();
            var periodStart = HabitSchedule.PeriodStartLocal(now, book.Period, tz);
            var totalInPeriod = totals.TryGetValue(periodStart, out var amount) ? amount : 0;
            if (totalInPeriod >= book.DailyGoalAmount)
            {
                continue;
            }

            var keyDate = periodStart.ToString("yyyy-MM-dd");
            var periodLabel = book.Period switch
            {
                HabitPeriod.Weekly => "haftalık",
                HabitPeriod.Monthly => "aylık",
                _ => "günlük"
            };

            var previousPeriodStart = HabitSchedule.PreviousPeriodStartLocal(periodStart, book.Period);
            var lostStreak = BookService.CountStreakFromTotals(
                totals, previousPeriodStart, book.DailyGoalAmount, book.CreatedAt, book.Period, tz);

            if (lostStreak > 0)
            {
                await notifications.TryEnqueueAsync(
                    book.UserId,
                    NotificationTypes.BookStreakBroken,
                    "Okuma serin bozuldu",
                    $"{book.Title} için {lostStreak} {periodLabel} okuma zincirin bozuldu. Bugün yeniden başlayabilirsin.",
                    habitId: null,
                    dedupKey: $"bookstreakbroken:{book.Id}:{keyDate}",
                    cancellationToken);
            }
            else
            {
                await notifications.TryEnqueueAsync(
                    book.UserId,
                    NotificationTypes.BookMissed,
                    "Kaçırılan okuma hedefi",
                    $"{book.Title} için bu dönem okuma hedefin tamamlanmadı.",
                    habitId: null,
                    dedupKey: $"bookmissed:{book.Id}:{keyDate}",
                    cancellationToken);
            }
        }
    }
}