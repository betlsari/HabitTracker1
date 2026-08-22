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

        var newHabitCompletion = _context.HabitCompletions.Add(new HabitCompletion
        {
            HabitId = habitId,
            CompletionDate = completionUtc,
            Amount = dto.Amount,
            XpEarned = xpEarned
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

        // Not: Bu endpoint şu an XP/streak/flower/pet etkilerini yeniden hesaplamıyor
        // (mevcut davranışla tutarlı). Amount değişikliği focus/water türü habit'lerde
        // pet/flower state'ini geriye dönük düzeltmez; gerekirse ayrı bir "recalculate"
        // akışı eklenmeli.
        completion.Amount = dto.Amount;
        completion.CompletionDate = DateTime.SpecifyKind(dto.CompletionDate, DateTimeKind.Utc);

        await _context.SaveChangesAsync();
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

        // YENİ: Focus completion silinirse, önceden verilen pet XP'si geri alınır.
        if (HabitCategories.IsFocus(habit.Category) && completion.Amount != 0)
        {
            await _petGrowthService.RemoveFocusXpAsync(userId!, completion.Amount);
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