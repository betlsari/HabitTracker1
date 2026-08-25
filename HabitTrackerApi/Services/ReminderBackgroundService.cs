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
        var habits = await _context.Habits.AsNoTracking().Where(h => !h.IsArchived).ToListAsync(cancellationToken);
        var books = await _context.Books.AsNoTracking().Where(b => !b.IsArchived).ToListAsync(cancellationToken);

        foreach (var habit in habits)
        {
            await ProcessHabitAsync(habit, utcNow, cancellationToken);
        }

        foreach (var book in books)
        {
            await ProcessBookAsync(book, utcNow, cancellationToken);
        }
    }

    private async Task ProcessHabitAsync(Habit habit, DateTime utcNow, CancellationToken cancellationToken)
    {
        var timeZoneId = await _context.Users.Where(u => u.Id == habit.UserId)
            .Select(u => u.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
        var tz = TimeZones.Resolve(timeZoneId);
        var localNow = TimeZones.ToLocal(utcNow, tz);
        var periodStart = HabitSchedule.PeriodStartLocal(utcNow, habit.Period, tz);
        var (startUtc, endUtc) = HabitSchedule.UtcRange(periodStart, habit.Period, tz);
        var amount = await _context.HabitCompletions
            .Where(c => c.HabitId == habit.Id && c.CompletionDate >= startUtc && c.CompletionDate < endUtc)
            .SumAsync(c => (int?)c.Amount, cancellationToken) ?? 0;

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
        var previousAmount = await _context.HabitCompletions
            .Where(c => c.HabitId == habit.Id && c.CompletionDate >= previousUtc && c.CompletionDate < previousEndUtc)
            .SumAsync(c => (int?)c.Amount, cancellationToken) ?? 0;
        if (previousAmount >= habit.DailyGoal)
        {
            await _notifications.TryEnqueueAsync(habit.UserId, NotificationTypes.StreakBroken, "Aliskanlik zinciri bozuldu",
                $"{habit.Name} icin onceki zincir bu donemde devam etmedi.", habit.Id,
                $"streakbroken:{habit.Id}:{periodStart:yyyyMMdd}", cancellationToken);
        }
    }

    private async Task ProcessBookAsync(Book book, DateTime utcNow, CancellationToken cancellationToken)
    {
        var timeZoneId = await _context.Users.Where(u => u.Id == book.UserId)
            .Select(u => u.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
        var tz = TimeZones.Resolve(timeZoneId);
        var periodStart = HabitSchedule.PeriodStartLocal(utcNow, book.Period, tz);
        var (startUtc, endUtc) = HabitSchedule.UtcRange(periodStart, book.Period, tz);
        var amount = await _context.BookReadingLogs
            .Where(l => l.BookId == book.Id && l.ReadDate >= startUtc && l.ReadDate < endUtc)
            .SumAsync(l => (int?)l.Amount, cancellationToken) ?? 0;

        if (!HabitSchedule.IsEndOfPeriodWindow(utcNow, book.Period, tz, _missedHour, 1) || amount >= book.DailyGoalAmount)
        {
            return;
        }

        await _notifications.TryEnqueueAsync(book.UserId, NotificationTypes.BookMissed, "Kacirilan kitap hedefi",
            $"{book.Title} icin bu donemin okuma hedefi tamamlanmadi.", null,
            $"bookmissed:{book.Id}:{periodStart:yyyyMMdd}", cancellationToken);

        var previousStart = HabitSchedule.PreviousPeriodStartLocal(periodStart, book.Period);
        var (previousUtc, previousEndUtc) = HabitSchedule.UtcRange(previousStart, book.Period, tz);
        var previousAmount = await _context.BookReadingLogs
            .Where(l => l.BookId == book.Id && l.ReadDate >= previousUtc && l.ReadDate < previousEndUtc)
            .SumAsync(l => (int?)l.Amount, cancellationToken) ?? 0;
        if (previousAmount >= book.DailyGoalAmount)
        {
            await _notifications.TryEnqueueAsync(book.UserId, NotificationTypes.BookStreakBroken, "Kitap zinciri bozuldu",
                $"{book.Title} icin onceki okuma zinciri bu donemde devam etmedi.", null,
                $"bookstreakbroken:{book.Id}:{periodStart:yyyyMMdd}", cancellationToken);
        }
    }
}
