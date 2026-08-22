using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Services;

namespace Controllers;

[ApiController]
[Route("api/habits/{habitId}/[controller]")]
[Authorize]
public class HabitCompletionsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly XpService _xpService;
    private readonly HabitProgressService _progressService;
    private readonly FlowerService _flowerService;
    private readonly BadgeService _badgeService;
    private readonly NotificationService _notificationService;
    private readonly PetGrowthService _petGrowthService;

    public HabitCompletionsController(
        AppDbContext context,
        UserManager<User> userManager,
        XpService xpService,
        HabitProgressService progressService,
        FlowerService flowerService,
        BadgeService badgeService,
        NotificationService notificationService,
        PetGrowthService petGrowthService)
    {
        _context = context;
        _userManager = userManager;
        _xpService = xpService;
        _progressService = progressService;
        _flowerService = flowerService;
        _badgeService = badgeService;
        _notificationService = notificationService;
        _petGrowthService = petGrowthService;
    }

    [HttpPost]
    public async Task<ActionResult<HabitCompletionDto>> CompleteHabit(int habitId, CreateCompletionDto dto)
    {
        var habit = await _context.Habits.FindAsync(habitId);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(userId!);
        var completionUtc = DateTime.SpecifyKind(dto.CompletionDate, DateTimeKind.Utc);
        var tz = TimeZones.Resolve(user?.TimeZoneId);

        // YENİ: Habit.TargetTime tanımlıysa, tamamlamanın o saatte/öncesinde
        // yapılıp yapılmadığı hesaplanır.
        var isOnTime = IsWithinTargetTime(habit, completionUtc, tz);

        var snapshot = await _progressService.GetCompletionSnapshotAsync(
            habit, completionUtc, dto.Amount, user?.TimeZoneId);

        var streakKept = snapshot.GoalJustReached && snapshot.PreviousPeriodGoalMet;
        int xpEarned = _xpService.CalculateCompletionXp(habit, dto.Amount, snapshot.TotalBeforeInPeriod, streakKept, isOnTime);

        int petStreakBonus = streakKept ? _xpService.GetStreakKeepBonus() : 0;

        var newHabitCompletion = _context.HabitCompletions.Add(new HabitCompletion
        {
            HabitId = habitId,
            CompletionDate = completionUtc,
            Amount = dto.Amount,
            XpEarned = xpEarned,
            PetStreakBonusXp = petStreakBonus,
            IsOnTime = isOnTime
        });

        if (user != null)
        {
            user.TotalXp += xpEarned;
            await _userManager.UpdateAsync(user);
        }

        await _context.SaveChangesAsync();

        Flower? flower = null;
        if (HabitCategories.IsWater(habit.Category) && dto.Amount > 0)
        {
            flower = await _flowerService.AddWaterAsync(userId!, dto.Amount);
        }

        if (HabitCategories.IsFocus(habit.Category) && dto.Amount > 0)
        {
            await _petGrowthService.AddFocusXpAsync(userId!, dto.Amount);
        }

        if (petStreakBonus > 0)
        {
            await _petGrowthService.AddStreakBonusXpAsync(userId!, petStreakBonus);
        }

        await _badgeService.EvaluateAfterCompletionAsync(userId!, habit, snapshot, flower);

        if (snapshot.GoalJustReached)
        {
            // DÜZELTİLDİ: Sabit tek bir mesaj yerine çeşitlendirilmiş
            // motivasyon mesajlarından biri rastgele seçiliyor.
            await _notificationService.TryEnqueueAsync(
                userId!,
                NotificationTypes.GoalReached,
                "Hedef tamamlandı",
                MotivationMessages.GoalReached(habit.Name),
                habit.Id,
                $"goal:{habit.Id}:{snapshot.PeriodStartLocal:yyyy-MM-dd}");
        }

        return ToDto(newHabitCompletion.Entity);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<HabitCompletionDto>>> GetHabitCompletions(int habitId)
    {
        var habit = await _context.Habits.FindAsync(habitId);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        return await _context.HabitCompletions
            .AsNoTracking()
            .Where(c => c.HabitId == habitId)
            .Select(c => new HabitCompletionDto
            {
                Id = c.Id,
                HabitId = c.HabitId,
                CompletionDate = c.CompletionDate,
                Amount = c.Amount,
                XpEarned = c.XpEarned,
                IsOnTime = c.IsOnTime
            })
            .ToListAsync();
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<HabitCompletionDto>> UpdateCompletion(int habitId, int id, CreateCompletionDto dto)
    {
        var completion = await _context.HabitCompletions.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (completion == null || completion.HabitId != habitId)
        {
            return NotFound();
        }
        var habit = await _context.Habits.FindAsync(completion.HabitId);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(userId!);

        // 1) ESKİ etkileri geri al (flower/focus/pet-streak-bonus/kullanıcı XP'si).
        var oldAmount = completion.Amount;
        var oldXp = completion.XpEarned;
        var oldPetStreakBonus = completion.PetStreakBonusXp;

        if (HabitCategories.IsWater(habit.Category) && oldAmount != 0)
        {
            await _flowerService.AddWaterAsync(userId!, -oldAmount);
        }
        if (HabitCategories.IsFocus(habit.Category) && oldAmount != 0)
        {
            await _petGrowthService.RemoveFocusXpAsync(userId!, oldAmount);
        }
        if (oldPetStreakBonus > 0)
        {
            await _petGrowthService.RemoveStreakBonusXpAsync(userId!, oldPetStreakBonus);
        }
        if (user != null && oldXp != 0)
        {
            user.TotalXp = Math.Max(0, user.TotalXp - oldXp);
            await _userManager.UpdateAsync(user);
        }

        // 2) Yeni ham değerleri kaydet.
        completion.Amount = dto.Amount;
        completion.CompletionDate = DateTime.SpecifyKind(dto.CompletionDate, DateTimeKind.Utc);
        completion.XpEarned = 0;
        completion.PetStreakBonusXp = 0;
        completion.IsOnTime = false;
        await _context.SaveChangesAsync();

        // 3) YENİ değerlerle snapshot'ı yeniden hesapla.
        var tz = TimeZones.Resolve(user?.TimeZoneId);
        var periodStart = HabitSchedule.PeriodStartLocalOfCompletion(completion.CompletionDate, habit.Period, tz);
        var totals = await _progressService.LoadPeriodTotalsAsync(habit.Id, habit.Period, tz);
        var totalInPeriodIncludingThis = totals.TryGetValue(periodStart, out var t) ? t : 0;
        var totalBeforeThis = totalInPeriodIncludingThis - completion.Amount;
        var previousStart = HabitSchedule.PreviousPeriodStartLocal(periodStart, habit.Period);
        var previousTotal = totals.TryGetValue(previousStart, out var pt) ? pt : 0;

        var goalJustReached = totalBeforeThis < habit.DailyGoal && totalInPeriodIncludingThis >= habit.DailyGoal;
        var previousPeriodGoalMet = previousTotal >= habit.DailyGoal;
        var streakKept = goalJustReached && previousPeriodGoalMet;
        var streakAfter = goalJustReached
            ? HabitProgressService.CountStreak(habit, totals, periodStart, tz)
            : HabitProgressService.CountStreak(habit, totals, HabitSchedule.PeriodStartLocal(DateTime.UtcNow, habit.Period, tz), tz);

        // YENİ: Güncellenen tarih üzerinden TargetTime kontrolü yeniden yapılır.
        var isOnTime = IsWithinTargetTime(habit, completion.CompletionDate, tz);

        var newXp = _xpService.CalculateCompletionXp(habit, completion.Amount, totalBeforeThis, streakKept, isOnTime);
        var newPetStreakBonus = streakKept ? _xpService.GetStreakKeepBonus() : 0;

        // 4) YENİ etkileri uygula.
        Flower? flower = null;
        if (HabitCategories.IsWater(habit.Category) && completion.Amount > 0)
        {
            flower = await _flowerService.AddWaterAsync(userId!, completion.Amount);
        }
        if (HabitCategories.IsFocus(habit.Category) && completion.Amount > 0)
        {
            await _petGrowthService.AddFocusXpAsync(userId!, completion.Amount);
        }
        if (newPetStreakBonus > 0)
        {
            await _petGrowthService.AddStreakBonusXpAsync(userId!, newPetStreakBonus);
        }

        if (user != null)
        {
            user.TotalXp += newXp;
            await _userManager.UpdateAsync(user);
        }

        completion.XpEarned = newXp;
        completion.PetStreakBonusXp = newPetStreakBonus;
        completion.IsOnTime = isOnTime;
        await _context.SaveChangesAsync();

        var snapshot = new CompletionSnapshot
        {
            TotalBeforeInPeriod = totalBeforeThis,
            TotalAfterInPeriod = totalInPeriodIncludingThis,
            GoalJustReached = goalJustReached,
            PreviousPeriodGoalMet = previousPeriodGoalMet,
            StreakAfter = streakAfter,
            PeriodStartLocal = periodStart
        };
        await _badgeService.EvaluateAfterCompletionAsync(userId!, habit, snapshot, flower);

        if (goalJustReached)
        {
            await _notificationService.TryEnqueueAsync(
                userId!,
                NotificationTypes.GoalReached,
                "Hedef tamamlandı",
                MotivationMessages.GoalReached(habit.Name),
                habit.Id,
                $"goal:{habit.Id}:{periodStart:yyyy-MM-dd}");
        }

        return ToDto(completion);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteCompletion(int habitId, int id)
    {
        var completion = await _context.HabitCompletions.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (completion == null || completion.HabitId != habitId)
        {
            return NotFound();
        }
        var habit = await _context.Habits.FindAsync(completion.HabitId);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        if (HabitCategories.IsWater(habit.Category) && completion.Amount != 0)
        {
            await _flowerService.AddWaterAsync(userId!, -completion.Amount);
        }

        if (HabitCategories.IsFocus(habit.Category) && completion.Amount != 0)
        {
            await _petGrowthService.RemoveFocusXpAsync(userId!, completion.Amount);
        }

        if (completion.PetStreakBonusXp > 0)
        {
            await _petGrowthService.RemoveStreakBonusXpAsync(userId!, completion.PetStreakBonusXp);
        }

        if (completion.XpEarned != 0)
        {
            var user = await _userManager.FindByIdAsync(userId!);
            if (user != null)
            {
                user.TotalXp = Math.Max(0, user.TotalXp - completion.XpEarned);
                await _userManager.UpdateAsync(user);
            }
        }

        _context.HabitCompletions.Remove(completion);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // YENİ: Habit.TargetTime tanımlıysa, verilen tamamlama zamanının (UTC) o
    // saatte veya öncesinde olup olmadığını kullanıcının yerel saatine göre
    // kontrol eder. TargetTime tanımlı değilse her zaman false döner.
    private static bool IsWithinTargetTime(Habit habit, DateTime completionUtc, TimeZoneInfo tz)
    {
        if (!habit.TargetTime.HasValue)
        {
            return false;
        }

        var local = TimeZones.ToLocal(completionUtc, tz);
        return TimeOnly.FromDateTime(local) <= habit.TargetTime.Value;
    }

    private static HabitCompletionDto ToDto(HabitCompletion completion) => new()
    {
        Id = completion.Id,
        HabitId = completion.HabitId,
        CompletionDate = completion.CompletionDate,
        Amount = completion.Amount,
        XpEarned = completion.XpEarned,
        IsOnTime = completion.IsOnTime
    };
}