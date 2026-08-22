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
[Route("api/[controller]")]
[Authorize]
public class HabitsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly XpService _xpService;
    private readonly HabitProgressService _progressService;
    private readonly FlowerService _flowerService;
    private readonly PetGrowthService _petGrowthService;

    public HabitsController(
        AppDbContext context,
        UserManager<User> userManager,
        XpService xpService,
        HabitProgressService progressService,
        FlowerService flowerService,
        PetGrowthService petGrowthService)
    {
        _context = context;
        _userManager = userManager;
        _xpService = xpService;
        _progressService = progressService;
        _flowerService = flowerService;
        _petGrowthService = petGrowthService;
    }

    // DÜZELTİLDİ: Sayfalama eklendi. Önceden kullanıcının tüm habit'leri tek
    // seferde dönüyordu; habit sayısı arttıkça bu ölçeklenmiyordu. page/pageSize
    // opsiyonel — verilmezse page=1, pageSize=50 (mevcut davranışa yakın, geniş
    // bir varsayılan) kullanılır.
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<HabitDto>>> GetHabits(int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = _context.Habits.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt);

        var totalCount = await query.CountAsync();
        var habits = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResultDto<HabitDto>
        {
            Items = habits.Select(ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HabitDto>> GetHabit(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);

        if (habit == null)
        {
            return NotFound();
        }

        return ToDto(habit);
    }

    [HttpPost]
    public async Task<ActionResult<HabitDto>> CreateHabit(CreateHabitDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // DÜZELTİLDİ: Benzersizlik kontrolü artık case-insensitive (ve baştaki/sondaki
        // boşluklara duyarsız). Önceden "Su" ile "su" farklı habit sayılıyordu; oysa
        // HabitCategories (IsWater/IsReading/IsFocus) kategori eşleştirmelerini zaten
        // case-insensitive yapıyordu — bu tutarsızlık kafa karıştırıyordu.
        var normalizedName = dto.Name.Trim();
        var habitExist = await _context.Habits.AnyAsync(h =>
            h.UserId == userId && h.Name.ToLower() == normalizedName.ToLower());
        if (habitExist)
        {
            return BadRequest("Bu isimde zaten bir alışkanlığınız var.");
        }

        var habit = new Habit
        {
            XpPerUnit = 1,
            XpBonusForGoal = 10,
            Name = normalizedName,
            Category = dto.Category,
            DailyGoal = dto.DailyGoal,
            Period = dto.Period,
            TargetTime = dto.TargetTime,
            ReminderTime = dto.ReminderTime,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Habits.Add(habit);
        await _context.SaveChangesAsync();
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.TotalXp += _xpService.GetHabitCreationXp();
            await _userManager.UpdateAsync(user);
        }

        return ToDto(habit);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<HabitDto>> UpdateHabit(int id, CreateHabitDto dto)
    {
        var habit = await _context.Habits.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        // YENİ: Güncellemede de aynı case-insensitive benzersizlik kontrolü
        // uygulanıyor (kendi kaydı hariç), Create ile tutarlı olsun diye.
        var normalizedName = dto.Name.Trim();
        var nameTakenByAnother = await _context.Habits.AnyAsync(h =>
            h.UserId == userId && h.Id != id && h.Name.ToLower() == normalizedName.ToLower());
        if (nameTakenByAnother)
        {
            return BadRequest("Bu isimde zaten bir alışkanlığınız var.");
        }

        habit.Name = normalizedName;
        habit.Category = dto.Category;
        habit.DailyGoal = dto.DailyGoal;
        habit.Period = dto.Period;
        habit.TargetTime = dto.TargetTime;
        habit.ReminderTime = dto.ReminderTime;

        await _context.SaveChangesAsync();
        return ToDto(habit);
    }

    // DÜZELTİLDİ: Önceden habit silindiğinde bağlı HabitCompletions cascade ile
    // veritabanından siliniyordu ama:
    //   1) Habit oluşturma sırasında verilen XpService.GetHabitCreationXp() XP'si
    //      kullanıcıdan hiç geri alınmıyordu,
    //   2) Tüm completion'ların kazandırdığı XpEarned toplamı kullanıcıda "hayalet
    //      XP" olarak kalıyordu,
    //   3) Streak korunduğu için pet'lere verilmiş PetStreakBonusXp geri alınmıyordu,
    //   4) Su/Odaklanma kategorisindeki habit'lerde flower/pet XP etkileri de
    //      (Flower.WaterAmount, Pet.Xp) geri alınmıyordu.
    // BookService/BooksController.DeleteBook'taki mantığa paralel olarak artık
    // silmeden önce tüm bu yan etkiler hesaplanıp geri alınıyor.
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteHabit(int id)
    {
        var habit = await _context.Habits.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        var completions = await _context.HabitCompletions
            .Where(c => c.HabitId == id)
            .ToListAsync();

        var totalXpFromCompletions = completions.Sum(c => c.XpEarned);
        var totalPetStreakBonus = completions.Sum(c => c.PetStreakBonusXp);
        var totalAmount = completions.Sum(c => c.Amount);

        // Su kategorisi: bu habit'e kayıtlı toplam su miktarı çiçekten geri alınır.
        if (HabitCategories.IsWater(habit.Category) && totalAmount != 0)
        {
            await _flowerService.AddWaterAsync(userId!, -totalAmount);
        }

        // Odaklanma kategorisi: pet'lere verilmiş focus XP'si geri alınır.
        if (HabitCategories.IsFocus(habit.Category) && totalAmount != 0)
        {
            await _petGrowthService.RemoveFocusXpAsync(userId!, totalAmount);
        }

        // Streak korunduğu için pet'lere verilmiş düz bonus XP geri alınır.
        if (totalPetStreakBonus > 0)
        {
            await _petGrowthService.RemoveStreakBonusXpAsync(userId!, totalPetStreakBonus);
        }

        _context.Habits.Remove(habit);
        await _context.SaveChangesAsync();

        // Habit oluşturma XP'si + tüm completion'lardan kazanılan XP kullanıcıdan düşülür.
        var totalXpToRemove = totalXpFromCompletions + _xpService.GetHabitCreationXp();
        if (totalXpToRemove != 0)
        {
            var user = await _userManager.FindByIdAsync(userId!);
            if (user != null)
            {
                user.TotalXp = Math.Max(0, user.TotalXp - totalXpToRemove);
                await _userManager.UpdateAsync(user);
            }
        }

        return NoContent();
    }

    [HttpGet("{habitId:int}/progress")]
    public async Task<ActionResult<HabitProgressDto>> GetProgress(int habitId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId);

        if (habit == null)
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(userId!);
        return await _progressService.GetProgressAsync(habit, user?.TimeZoneId);
    }

    [HttpGet("{habitId:int}/stats")]
    public async Task<ActionResult<IEnumerable<DailyStatDto>>> GetStats(int habitId, int days = 7)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId);

        if (habit == null)
        {
            return NotFound();
        }

        if (days <= 0)
        {
            return BadRequest("days parametresi 1 veya daha büyük olmalıdır.");
        }

        var user = await _userManager.FindByIdAsync(userId!);
        return await _progressService.GetStatsAsync(habit, user?.TimeZoneId, days);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<HabitProgressDto>>> GetSummary()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habits = await _context.Habits.AsNoTracking()
            .Where(h => h.UserId == userId)
            .ToListAsync();

        var user = await _userManager.FindByIdAsync(userId!);
        var result = await _progressService.GetSummaryAsync(habits, user?.TimeZoneId);
        return Ok(result);
    }


    [HttpGet("comparison")]
    public async Task<ActionResult<IEnumerable<HabitComparisonDto>>> GetComparison(int lookbackPeriods = 30)
    {
        if (lookbackPeriods <= 0)
        {
            return BadRequest("lookbackPeriods parametresi 1 veya daha büyük olmalıdır.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habits = await _context.Habits.AsNoTracking()
            .Where(h => h.UserId == userId)
            .ToListAsync();

        if (habits.Count == 0)
        {
            return Ok(new List<HabitComparisonDto>());
        }

        var user = await _userManager.FindByIdAsync(userId!);
        var result = await _progressService.GetComparisonAsync(habits, user?.TimeZoneId, lookbackPeriods);
        return Ok(result);
    }

    private static HabitDto ToDto(Habit habit) => new()
    {
        Id = habit.Id,
        Name = habit.Name,
        Category = habit.Category,
        DailyGoal = habit.DailyGoal,
        CreatedAt = habit.CreatedAt,
        XpPerUnit = habit.XpPerUnit,
        XpBonusForGoal = habit.XpBonusForGoal,
        Period = habit.Period,
        TargetTime = habit.TargetTime,
        ReminderTime = habit.ReminderTime
    };
}