// HabitTrackerApi/Controllers/HabitsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Services;
using Microsoft.Extensions.Options;
using Configuration;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HabitsController : ControllerBase
{
    private const int MaxHabitsPerUser = 100;
    private const int MaxStatsPeriods = 366;
    private const int MaxComparisonPeriods = 366;

    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "createdAt", "name", "category"
    };

    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly XpService _xpService;
    private readonly HabitProgressService _progressService;
    private readonly FlowerService _flowerService;
    private readonly PetGrowthService _petGrowthService;
    private readonly BadgeService _badgeService;
    private readonly int _maxHabitsPerUser;

    public HabitsController(
        AppDbContext context,
        UserManager<User> userManager,
        XpService xpService,
        HabitProgressService progressService,
        FlowerService flowerService,
        PetGrowthService petGrowthService,
        BadgeService badgeService,
        IOptions<AppLimitsOptions> limits)
    {
        _context = context;
        _userManager = userManager;
        _xpService = xpService;
        _progressService = progressService;
        _flowerService = flowerService;
        _petGrowthService = petGrowthService;
        _badgeService = badgeService;
        _maxHabitsPerUser = limits.Value.MaxHabitsPerUser;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<HabitDto>>> GetHabits(
        int page = 1,
        int pageSize = 50,
        string? search = null,
        string? category = null,
        bool includeArchived = false,
        string sortBy = "createdAt",
        bool sortDesc = true)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;
        if (!AllowedSortFields.Contains(sortBy)) sortBy = "createdAt";

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = _context.Habits.AsNoTracking().Where(h => h.UserId == userId);

        if (!includeArchived)
        {
            query = query.Where(h => !h.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(h => EF.Functions.ILike(h.Name, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(h => h.Category == category);
        }

        query = (sortBy.ToLowerInvariant(), sortDesc) switch
        {
            ("name", true) => query.OrderByDescending(h => h.Name),
            ("name", false) => query.OrderBy(h => h.Name),
            ("category", true) => query.OrderByDescending(h => h.Category),
            ("category", false) => query.OrderBy(h => h.Category),
            (_, false) => query.OrderBy(h => h.CreatedAt),
            _ => query.OrderByDescending(h => h.CreatedAt)
        };

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

    [HttpGet("categories")]
    public ActionResult<IEnumerable<string>> GetAllowedCategories()
    {
        return Ok(HabitCategories.Allowed);
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

    // DÜZELTİLDİ (🔴 race condition): "mevcut habit sayısını say -> limiti
    // aşıyorsa reddet -> isim çakışması var mı diye kontrol et -> ekle"
    // adımları arasında hiçbir eşzamanlılık koruması yoktu. Aynı kullanıcıdan
    // gelen iki eşzamanlı istek hem MaxHabitsPerUser limitini aşabiliyor hem
    // de (DB'deki unique index'e rağmen) 500 hatasıyla sonuçlanabiliyordu.
    // PetsController.CreatePet / DevicesController.Register ile aynı desende
    // kullanıcıya özel bir Postgres advisory lock eklendi; bu sayede aynı
    // kullanıcının habit oluşturma istekleri serileştirilir.
    [HttpPost]
    public async Task<ActionResult<HabitDto>> CreateHabit(CreateHabitDto dto)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult<HabitDto>>(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({"habit:" + userId}))");

            if (await _context.Habits.CountAsync(h => h.UserId == userId) >= _maxHabitsPerUser)
            {
                return Conflict($"En fazla {_maxHabitsPerUser} alışkanlık oluşturabilirsiniz.");
            }

            if (!HabitCategories.IsValid(dto.Category))
            {
                return BadRequest($"Geçersiz kategori. İzin verilen kategoriler: {string.Join(", ", HabitCategories.Allowed)}");
            }

            var normalizedName = dto.Name.Trim();
            var normalizedNameKey = normalizedName.ToUpperInvariant();
            var habitExist = await _context.Habits.AnyAsync(h =>
                h.UserId == userId && h.NormalizedName == normalizedNameKey);
            if (habitExist)
            {
                return BadRequest("Bu isimde zaten bir alışkanlığınız var.");
            }

            var habit = new Habit
            {
                XpPerUnit = 1,
                XpBonusForGoal = 10,
                Name = normalizedName,
                NormalizedName = normalizedNameKey,
                Category = dto.Category,
                DailyGoal = dto.DailyGoal,
                Period = dto.Period,
                TargetTime = dto.TargetTime,
                ReminderTime = dto.ReminderTime,
                Notes = dto.Notes,
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

            await transaction.CommitAsync();
            return ToDto(habit);
        });
    }

    // DÜZELTİLDİ: Kategori değişimi artık transaction içinde ele alınıyor
    // ve geçmiş tamamlamalardan kaynaklanan çiçek suyu / pet odaklanma XP'si
    // eski kategoriden geri alınıp yeni kategoriye göre yeniden uygulanıyor.
    // Önceden ör. "Su" -> "Spor" geçişinde geçmişte sulanmış çiçek asla geri
    // alınmıyor, "Spor" -> "Odaklanma" geçişinde ise geçmiş miktarlar pet'e
    // hiç XP olarak yansımıyordu.
    //
    // DÜZELTİLDİ (🟡 rozet tutarsızlığı): Bir habit "Su" kategorisine
    // taşındığında ve geçmiş miktar çiçeğe toplu olarak eklendiğinde çiçek
    // seviyesi doğrudan 5/10'a atlayabiliyordu, ama bu yol normal
    // AddWaterAsync (tekil completion) akışının çağırdığı
    // BadgeService.EvaluateAfterCompletionAsync'i hiç tetiklemiyordu; bu da
    // WATER_GROWTH_5 / WATER_GROWTH_10 rozetlerinin retroaktif olarak asla
    // verilmemesine yol açıyordu. Artık kategori "Su"ya çevrildiğinde çiçek
    // seviyesine göre bu rozetler de değerlendiriliyor.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<HabitDto>> UpdateHabit(int id, CreateHabitDto dto)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult<HabitDto>>(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var habit = await _context.Habits.FindAsync(id);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (habit == null || habit.UserId != userId)
            {
                return NotFound();
            }

            if (!HabitCategories.IsValid(dto.Category))
            {
                return BadRequest($"Geçersiz kategori. İzin verilen kategoriler: {string.Join(", ", HabitCategories.Allowed)}");
            }

            var normalizedName = dto.Name.Trim();
            var normalizedNameKey = normalizedName.ToUpperInvariant();
            var nameTakenByAnother = await _context.Habits.AnyAsync(h =>
                 h.UserId == userId && h.Id != id && h.NormalizedName == normalizedNameKey);
            if (nameTakenByAnother)
            {
                return BadRequest("Bu isimde zaten bir alışkanlığınız var.");
            }

            var goalOrScheduleChanged = habit.DailyGoal != dto.DailyGoal
                || habit.Period != dto.Period
                || habit.TargetTime != dto.TargetTime;

            var oldCategory = habit.Category;
            var categoryChanged = !string.Equals(oldCategory, dto.Category, StringComparison.OrdinalIgnoreCase);

            habit.Name = normalizedName;
            habit.NormalizedName = normalizedNameKey;
            habit.Category = dto.Category;
            habit.DailyGoal = dto.DailyGoal;
            habit.Period = dto.Period;
            habit.TargetTime = dto.TargetTime;
            habit.ReminderTime = dto.ReminderTime;
            habit.Notes = dto.Notes;

            await _context.SaveChangesAsync();

            // YENİ: Kategori değişince eski kategoriye bağlı yan etkiler
            // (çiçek suyu / pet odaklanma XP'si) geçmiş toplam miktar
            // üzerinden geri alınır, yeni kategoriye bağlıysa yeniden uygulanır.
            if (categoryChanged)
            {
                var totalAmount = await _context.HabitCompletions
                    .Where(c => c.HabitId == id)
                    .SumAsync(c => (int?)c.Amount) ?? 0;

                if (totalAmount != 0)
                {
                    var wasWater = HabitCategories.IsWater(oldCategory);
                    var isWater = HabitCategories.IsWater(habit.Category);
                    var wasFocus = HabitCategories.IsFocus(oldCategory);
                    var isFocus = HabitCategories.IsFocus(habit.Category);

                    if (wasWater && !isWater)
                    {
                        await _flowerService.AddWaterAsync(userId!, -totalAmount);
                    }
                    else if (!wasWater && isWater)
                    {
                        var flower = await _flowerService.AddWaterAsync(userId!, totalAmount);
                        await _badgeService.EvaluateFlowerBadgesAsync(userId!, flower.Level);
                    }

                    if (wasFocus && !isFocus)
                    {
                        await _petGrowthService.RemoveFocusXpAsync(userId!, totalAmount);
                    }
                    else if (!wasFocus && isFocus)
                    {
                        await _petGrowthService.AddFocusXpAsync(userId!, totalAmount);
                    }
                }
            }

            if (goalOrScheduleChanged)
            {
                var user = await _userManager.FindByIdAsync(userId!);
                var recalc = await _progressService.RecalculateHabitAsync(habit, user?.TimeZoneId);

                if (recalc.XpDelta != 0 && user != null)
                {
                    user.TotalXp = Math.Max(0, user.TotalXp + recalc.XpDelta);
                    await _userManager.UpdateAsync(user);
                }

                if (recalc.PetStreakBonusDelta > 0)
                {
                    await _petGrowthService.AddStreakBonusXpAsync(userId!, recalc.PetStreakBonusDelta);
                }
                else if (recalc.PetStreakBonusDelta < 0)
                {
                    await _petGrowthService.RemoveStreakBonusXpAsync(userId!, -recalc.PetStreakBonusDelta);
                }
            }

            await transaction.CommitAsync();
            return ToDto(habit);
        });
    }

    [HttpPut("{id:int}/archive")]
    public async Task<ActionResult<HabitDto>> ArchiveHabit(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.FindAsync(id);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        if (!habit.IsArchived)
        {
            habit.IsArchived = true;
            habit.ArchivedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return ToDto(habit);
    }

    [HttpPut("{id:int}/unarchive")]
    public async Task<ActionResult<HabitDto>> UnarchiveHabit(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.FindAsync(id);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        if (habit.IsArchived)
        {
            habit.IsArchived = false;
            habit.ArchivedAt = null;
            await _context.SaveChangesAsync();
        }

        return ToDto(habit);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteHabit(int id)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult>(async () =>
        {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync();

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

        if (HabitCategories.IsWater(habit.Category) && totalAmount != 0)
        {
            await _flowerService.AddWaterAsync(userId!, -totalAmount);
        }

        if (HabitCategories.IsFocus(habit.Category) && totalAmount != 0)
        {
            await _petGrowthService.RemoveFocusXpAsync(userId!, totalAmount);
        }

        if (totalPetStreakBonus > 0)
        {
            await _petGrowthService.RemoveStreakBonusXpAsync(userId!, totalPetStreakBonus);
        }

        _context.Habits.Remove(habit);
        await _context.SaveChangesAsync();

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

        await transaction.CommitAsync();
        return NoContent();
        });
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
    public async Task<ActionResult<IEnumerable<DailyStatDto>>> GetStats(int habitId, int days = 7, string? granularity = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId);

        if (habit == null)
        {
            return NotFound();
        }

        if (days is <= 0 or > MaxStatsPeriods)
        {
            return BadRequest($"days parametresi 1 ile {MaxStatsPeriods} arasında olmalıdır.");
        }

        HabitPeriod? parsedGranularity = null;
        if (!string.IsNullOrWhiteSpace(granularity))
        {
            if (!Enum.TryParse<HabitPeriod>(granularity, true, out var g))
            {
                return BadRequest("granularity parametresi Daily, Weekly veya Monthly olmalıdır.");
            }
            parsedGranularity = g;
        }

        var user = await _userManager.FindByIdAsync(userId!);
        return await _progressService.GetStatsAsync(habit, user?.TimeZoneId, days, parsedGranularity);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<HabitProgressDto>>> GetSummary(bool includeArchived = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = _context.Habits.AsNoTracking().Where(h => h.UserId == userId);
        if (!includeArchived)
        {
            query = query.Where(h => !h.IsArchived);
        }
        var habits = await query.ToListAsync();

        var user = await _userManager.FindByIdAsync(userId!);
        var result = await _progressService.GetSummaryAsync(habits, user?.TimeZoneId);
        return Ok(result);
    }

    [HttpGet("comparison")]
    public async Task<ActionResult<IEnumerable<HabitComparisonDto>>> GetComparison(int lookbackPeriods = 30, bool includeArchived = false)
    {
        if (lookbackPeriods is <= 0 or > MaxComparisonPeriods)
        {
            return BadRequest($"lookbackPeriods parametresi 1 ile {MaxComparisonPeriods} arasında olmalıdır.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = _context.Habits.AsNoTracking().Where(h => h.UserId == userId);
        if (!includeArchived)
        {
            query = query.Where(h => !h.IsArchived);
        }
        var habits = await query.ToListAsync();

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
        ReminderTime = habit.ReminderTime,
        IsArchived = habit.IsArchived,
        ArchivedAt = habit.ArchivedAt,
        Notes = habit.Notes
    };
}