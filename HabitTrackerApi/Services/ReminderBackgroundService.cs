// HabitTrackerApi/Services/ReminderBackgroundService.cs
using Configuration;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Models;

namespace Services;

public sealed class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderBackgroundService> _logger;

    public ReminderBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ReminderService>();
                await service.ProcessAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hatirlatma ve kacirilan aliskanlik bildirimleri islenirken hata olustu.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}

public sealed class ReminderService
{
    // DÜZELTİLDİ (🔴 madde 1 — sınırsız bellek yükleme): Önceden ProcessAsync
    // TÜM arşivlenmemiş habit'leri + book'ları + 93 günlük TÜM
    // completion/reading-log kayıtlarını tek seferde belleğe çekiyordu. Bu
    // servis HER DAKİKA çalıştığı için kullanıcı/habit sayısı arttıkça bu,
    // sistemin en pahalı periyodik işi haline geliyordu (hem DB'den transfer
    // edilen veri hacmi hem de bellek kullanımı sınırsız büyüyordu).
    //
    // Artık habit'ler ve book'lar BatchSize (300) büyüklüğünde sayfalar
    // halinde, Id'ye göre keyset pagination ile işleniyor. Her sayfa için
    // SADECE o sayfadaki habit/book Id'lerine ait 93 günlük aktivite
    // çekiliyor — böylece bellekte aynı anda en fazla ~300 habit/book +
    // onlara ait aktivite kayıtları tutuluyor, tüm tablo değil.
    private const int BatchSize = 300;

    private readonly AppDbContext _context;
    private readonly NotificationService _notifications;
    private readonly int _missedHour;

    public ReminderService(AppDbContext context, NotificationService notifications, IConfiguration configuration)
    {
        _context = context;
        _notifications = notifications;
        _missedHour = Math.Clamp(configuration.GetValue("Notifications:MissedHabitLocalHour", 21), 0, 23);
    }

    public async Task ProcessAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var activitySinceUtc = utcNow.AddDays(-93);

        await ProcessHabitsInBatchesAsync(utcNow, activitySinceUtc, cancellationToken);
        await ProcessBooksInBatchesAsync(utcNow, activitySinceUtc, cancellationToken);
    }

    private async Task ProcessHabitsInBatchesAsync(DateTime utcNow, DateTime activitySinceUtc, CancellationToken cancellationToken)
    {
        var lastId = 0;

        while (true)
        {
            var habitBatch = await _context.Habits.AsNoTracking()
                .Where(h => !h.IsArchived && h.Id > lastId)
                .OrderBy(h => h.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (habitBatch.Count == 0)
            {
                break;
            }

            lastId = habitBatch[^1].Id;

            var userIds = habitBatch.Select(h => h.UserId).Distinct().ToArray();
            var timeZones = await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.TimeZoneId, cancellationToken);

            var habitIds = habitBatch.Select(h => h.Id).ToArray();
            var habitAmounts = (await _context.HabitCompletions.AsNoTracking()
                .Where(c => habitIds.Contains(c.HabitId) && c.CompletionDate >= activitySinceUtc)
                .Select(c => new ActivityAmount(c.HabitId, c.CompletionDate, c.Amount))
                .ToListAsync(cancellationToken)).ToLookup(x => x.Id);

            foreach (var habit in habitBatch)
            {
                timeZones.TryGetValue(habit.UserId, out var timeZoneId);
                await ProcessHabitAsync(habit, utcNow, timeZoneId, habitAmounts[habit.Id], cancellationToken);
            }
        }
    }

    private async Task ProcessBooksInBatchesAsync(DateTime utcNow, DateTime activitySinceUtc, CancellationToken cancellationToken)
    {
        var lastId = 0;

        while (true)
        {
            var bookBatch = await _context.Books.AsNoTracking()
                .Where(b => !b.IsArchived && b.Id > lastId)
                .OrderBy(b => b.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (bookBatch.Count == 0)
            {
                break;
            }

            lastId = bookBatch[^1].Id;

            var userIds = bookBatch.Select(b => b.UserId).Distinct().ToArray();
            var timeZones = await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.TimeZoneId, cancellationToken);

            var bookIds = bookBatch.Select(b => b.Id).ToArray();
            var bookAmounts = (await _context.BookReadingLogs.AsNoTracking()
                .Where(l => bookIds.Contains(l.BookId) && l.ReadDate >= activitySinceUtc)
                .Select(l => new ActivityAmount(l.BookId, l.ReadDate, l.Amount))
                .ToListAsync(cancellationToken)).ToLookup(x => x.Id);

            foreach (var book in bookBatch)
            {
                timeZones.TryGetValue(book.UserId, out var timeZoneId);
                await ProcessBookAsync(book, utcNow, timeZoneId, bookAmounts[book.Id], cancellationToken);
            }
        }
    }

    private async Task ProcessHabitAsync(Habit habit, DateTime utcNow, string? timeZoneId,
        IEnumerable<ActivityAmount> activities, CancellationToken cancellationToken)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var localNow = TimeZones.ToLocal(utcNow, tz);
        var periodStart = HabitSchedule.PeriodStartLocal(utcNow, habit.Period, tz);
        var (startUtc, endUtc) = HabitSchedule.UtcRange(periodStart, habit.Period, tz);
        var amount = activities.Where(c => c.AtUtc >= startUtc && c.AtUtc < endUtc).Sum(c => c.Amount);

        if (habit.ReminderTime is { } reminder && localNow.Hour == reminder.Hour && localNow.Minute == reminder.Minute && amount < habit.DailyGoal)
        {
            await _notifications.TryEnqueueAsync(habit.UserId, NotificationTypes.Reminder, "Aliskanlik hatirlatmasi",
                $"{habit.Name} icin donem hedefinize {habit.DailyGoal - amount} kaldi.", habit.Id,
                $"reminder:{habit.Id}:{periodStart:yyyyMMdd}", cancellationToken);
        }

        if (!HabitSchedule.IsEndOfPeriodWindow(utcNow, habit.Period, tz, _missedHour, 1) || amount >= habit.DailyGoal)
        {
            return;
        }

        await _notifications.TryEnqueueAsync(habit.UserId, NotificationTypes.Missed, "Kacirilan aliskanlik",
            $"{habit.Name} icin bu donemin hedefi tamamlanmadi.", habit.Id,
            $"missed:{habit.Id}:{periodStart:yyyyMMdd}", cancellationToken);

        var previousStart = HabitSchedule.PreviousPeriodStartLocal(periodStart, habit.Period);
        var (previousUtc, previousEndUtc) = HabitSchedule.UtcRange(previousStart, habit.Period, tz);
        var previousAmount = activities.Where(c => c.AtUtc >= previousUtc && c.AtUtc < previousEndUtc).Sum(c => c.Amount);
        if (previousAmount >= habit.DailyGoal)
        {
            await _notifications.TryEnqueueAsync(habit.UserId, NotificationTypes.StreakBroken, "Aliskanlik zinciri bozuldu",
                $"{habit.Name} icin onceki zincir bu donemde devam etmedi.", habit.Id,
                $"streakbroken:{habit.Id}:{periodStart:yyyyMMdd}", cancellationToken);
        }
    }

    private async Task ProcessBookAsync(Book book, DateTime utcNow, string? timeZoneId,
        IEnumerable<ActivityAmount> activities, CancellationToken cancellationToken)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var periodStart = HabitSchedule.PeriodStartLocal(utcNow, book.Period, tz);
        var (startUtc, endUtc) = HabitSchedule.UtcRange(periodStart, book.Period, tz);
        var amount = activities.Where(l => l.AtUtc >= startUtc && l.AtUtc < endUtc).Sum(l => l.Amount);

        if (!HabitSchedule.IsEndOfPeriodWindow(utcNow, book.Period, tz, _missedHour, 1) || amount >= book.DailyGoalAmount)
        {
            return;
        }

        await _notifications.TryEnqueueAsync(book.UserId, NotificationTypes.BookMissed, "Kacirilan kitap hedefi",
            $"{book.Title} icin bu donemin okuma hedefi tamamlanmadi.", null,
            $"bookmissed:{book.Id}:{periodStart:yyyyMMdd}", cancellationToken);

        var previousStart = HabitSchedule.PreviousPeriodStartLocal(periodStart, book.Period);
        var (previousUtc, previousEndUtc) = HabitSchedule.UtcRange(previousStart, book.Period, tz);
        var previousAmount = activities.Where(l => l.AtUtc >= previousUtc && l.AtUtc < previousEndUtc).Sum(l => l.Amount);
        if (previousAmount >= book.DailyGoalAmount)
        {
            await _notifications.TryEnqueueAsync(book.UserId, NotificationTypes.BookStreakBroken, "Kitap zinciri bozuldu",
                $"{book.Title} icin onceki okuma zinciri bu donemde devam etmedi.", null,
                $"bookstreakbroken:{book.Id}:{periodStart:yyyyMMdd}", cancellationToken);
        }
    }

    private sealed record ActivityAmount(int Id, DateTime AtUtc, int Amount);
}