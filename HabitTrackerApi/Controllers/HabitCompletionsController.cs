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
        var snapshot = await _progressService.GetCompletionSnapshotAsync(
            habit, completionUtc, dto.Amount, user?.TimeZoneId);

        var streakKept = snapshot.GoalJustReached && snapshot.PreviousPeriodGoalMet;
        int xpEarned = _xpService.CalculateCompletionXp(habit, dto.Amount, snapshot.TotalBeforeInPeriod, streakKept);

        // YENİ: Streak korunduğunda, habit kategorisi ne olursa olsun kullanıcının
        // pet'lerine de düz bir bonus XP veriliyor (bkz. PetGrowthService.AddStreakBonusXpAsync).
        int petStreakBonus = streakKept ? _xpService.GetStreakKeepBonus() : 0;

        var newHabitCompletion = _context.HabitCompletions.Add(new HabitCompletion
        {
            HabitId = habitId,
            CompletionDate = completionUtc,
            Amount = dto.Amount,
            XpEarned = xpEarned,
            PetStreakBonusXp = petStreakBonus
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

        // YENİ: Odaklanma/çalışma habit'i tamamlandığında kullanıcının pet(ler)i
        // doğrudan XP kazanır (bkz. PetGrowthService, dokümandaki "odaklanma süresi
        // ile hayvan büyütme" özelliği).
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
            var periodLabel = habit.Period switch
            {
                HabitPeriod.Weekly => "haftalık",
                HabitPeriod.Monthly => "aylık",
                _ => "günlük"
            };
            await _notificationService.TryEnqueueAsync(
                userId!,
                NotificationTypes.GoalReached,
                "Hedef tamamlandı",
                $"{habit.Name} için {periodLabel} hedefini tutturdun. Tebrikler!",
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
                XpEarned = c.XpEarned
            })
            .ToListAsync();
    }

    // DÜZELTİLDİ: Artık Amount/CompletionDate değişikliğinde XP, streak (pet bonus
    // dahil), flower ve focus/pet etkilerini TAMAMEN yeniden hesaplıyor. Önceden
    // bu endpoint sadece Amount/CompletionDate güncelliyor, XpEarned alanı bozuk
    // kalıyordu ve diğer tüm yan etkiler senkron dışı kalıyordu.
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

        // 2) Yeni ham değerleri kaydet (totals hesaplamasının DB'den doğru
        // okunabilmesi için önce bunları persist ediyoruz).
        completion.Amount = dto.Amount;
        completion.CompletionDate = DateTime.SpecifyKind(dto.CompletionDate, DateTimeKind.Utc);
        completion.XpEarned = 0;
        completion.PetStreakBonusXp = 0;
        await _context.SaveChangesAsync();

        // 3) YENİ değerlerle snapshot'ı yeniden hesapla (bu completion'ın kendi
        // dönemindeki toplam içinde payını çıkararak "bu kayıttan önceki toplam"ı
        // buluyoruz — Create akışındaki mantıkla tutarlı).
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

        var newXp = _xpService.CalculateCompletionXp(habit, completion.Amount, totalBeforeThis, streakKept);
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
            var periodLabel = habit.Period switch
            {
                HabitPeriod.Weekly => "haftalık",
                HabitPeriod.Monthly => "aylık",
                _ => "günlük"
            };
            await _notificationService.TryEnqueueAsync(
                userId!,
                NotificationTypes.GoalReached,
                "Hedef tamamlandı",
                $"{habit.Name} için {periodLabel} hedefini tutturdun. Tebrikler!",
                habit.Id,
                $"goal:{habit.Id}:{periodStart:yyyy-MM-dd}");
        }

        return ToDto(completion);
    }

    // DÜZELTİLDİ: Artık kullanıcının TotalXp'sinden (ve varsa pet streak bonusundan)
    // bu completion'ın kazandırdığı miktar düşülüyor. Önceden sadece flower/pet
    // focus etkileri geri alınıyor, XP kullanıcıda "sahte" olarak kalıyordu.
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

        // YENİ: Focus completion silinirse, önceden verilen pet XP'si geri alınır.
        if (HabitCategories.IsFocus(habit.Category) && completion.Amount != 0)
        {
            await _petGrowthService.RemoveFocusXpAsync(userId!, completion.Amount);
        }

        // YENİ: Streak korunduğu için verilmiş pet bonus XP'si de geri alınır.
        if (completion.PetStreakBonusXp > 0)
        {
            await _petGrowthService.RemoveStreakBonusXpAsync(userId!, completion.PetStreakBonusXp);
        }

        // YENİ: Kullanıcının genel TotalXp'sinden bu completion'ın kazandırdığı XP düşülür.
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

    private static HabitCompletionDto ToDto(HabitCompletion completion) => new()
    {
        Id = completion.Id,
        HabitId = completion.HabitId,
        CompletionDate = completion.CompletionDate,
        Amount = completion.Amount,
        XpEarned = completion.XpEarned
    };
}