using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

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
                var missedHour = _configuration.GetValue("Notifications:MissedHabitLocalHour", 21);

                await SendRemindersAsync(db, notifications, progress, stoppingToken);
                await SendMissedAsync(db, notifications, progress, missedHour, stoppingToken);
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

    private static async Task SendRemindersAsync(
        AppDbContext db,
        NotificationService notifications,
        HabitProgressService progress,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var habits = await db.Habits.AsNoTracking()
            .Include(h => h.User)
            .Where(h => h.ReminderTime != null)
            .ToListAsync(cancellationToken);

        foreach (var habit in habits)
        {
            var tz = TimeZones.Resolve(habit.User?.TimeZoneId);
            var local = TimeZones.ToLocal(now, tz);
            var reminder = habit.ReminderTime!.Value;
            var reminderTime = reminder.ToTimeSpan();
            if (Math.Abs((local.TimeOfDay - reminderTime).TotalMinutes) > MinuteTolerance)
            {
                continue;
            }

            var periodStart = HabitSchedule.PeriodStartLocal(now, habit.Period, tz);
            var totals = await progress.LoadPeriodTotalsAsync(habit.Id, habit.Period, tz, cancellationToken);
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

    // DÜZELTİLDİ: Önceden dönem sonunda hedef tutturulamadığında her zaman aynı
    // jenerik "Missed" bildirimi gönderiliyordu — kullanıcının önceden aktif bir
    // serisi (streak) olup olmadığına hiç bakılmıyordu. Artık bir önceki dönemde
    // hedef tutturulmuşsa (yani gerçekten bir zincir kırıldıysa) ayrı bir
    // "StreakBroken" bildirimi, kaç dönemlik zincirin bozulduğunu belirterek
    // gönderiliyor. Aktif bir seri yoksa (zaten kırılacak bir şey yoksa) eski
    // jenerik "Missed" bildirimi kullanılmaya devam ediyor.
    private static async Task SendMissedAsync(
        AppDbContext db,
        NotificationService notifications,
        HabitProgressService progress,
        int missedHour,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var habits = await db.Habits.AsNoTracking()
            .Include(h => h.User)
            .ToListAsync(cancellationToken);

        foreach (var habit in habits)
        {
            var tz = TimeZones.Resolve(habit.User?.TimeZoneId);
            if (!HabitSchedule.IsEndOfPeriodWindow(now, habit.Period, tz, missedHour, MinuteTolerance))
            {
                continue;
            }

            var periodStart = HabitSchedule.PeriodStartLocal(now, habit.Period, tz);
            var totals = await progress.LoadPeriodTotalsAsync(habit.Id, habit.Period, tz, cancellationToken);
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

            // YENİ: Bir önceki dönemde streak var mıydı? Varsa bu, tam olarak
            // "bozulan bir zincir" demektir.
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
}