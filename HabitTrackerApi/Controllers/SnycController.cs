using Data;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using System.Security.Claims;

namespace Controllers;


[ApiController]
[Route("api/sync")]
[Authorize]
public class SyncController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly XpService _xpService;
    private readonly HabitProgressService _progressService;
    private readonly FlowerService _flowerService;
    private readonly PetGrowthService _petGrowthService;
    private readonly BadgeService _badgeService;
    private readonly NotificationService _notificationService;
    private readonly BookService _bookService;

    public SyncController(
        AppDbContext context,
        UserManager<User> userManager,
        XpService xpService,
        HabitProgressService progressService,
        FlowerService flowerService,
        PetGrowthService petGrowthService,
        BadgeService badgeService,
        NotificationService notificationService,
        BookService bookService)
    {
        _context = context;
        _userManager = userManager;
        _xpService = xpService;
        _progressService = progressService;
        _flowerService = flowerService;
        _petGrowthService = petGrowthService;
        _badgeService = badgeService;
        _notificationService = notificationService;
        _bookService = bookService;
    }

    [HttpPost("batch")]
    public async Task<ActionResult<BatchSyncResultDto>> SyncBatch(BatchSyncRequestDto request)
    {
        var totalItems = request.HabitCompletions.Count + request.BookReadingLogs.Count;
        if (totalItems == 0)
        {
            return BadRequest("En az bir öğe gönderilmelidir.");
        }
        if (totalItems > BatchSyncRequestDto.MaxItems)
        {
            return BadRequest($"Tek istekte en fazla {BatchSyncRequestDto.MaxItems} öğe gönderilebilir.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);

        var result = new BatchSyncResultDto();

        foreach (var item in request.HabitCompletions)
        {
            result.HabitCompletions.Add(await ProcessHabitCompletionAsync(userId, user, item));
        }

        foreach (var item in request.BookReadingLogs)
        {
            result.BookReadingLogs.Add(await ProcessBookReadingLogAsync(userId, user, item));
        }

        return Ok(result);
    }

    private async Task<BatchItemResultDto> ProcessHabitCompletionAsync(
        string userId, User? user, BatchHabitCompletionItemDto item)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync();

                // Idempotency: aynı habit + ClientRequestId zaten işlenmişse
                // var olan kaydı döndür, tekrar oluşturma.
                var existing = await _context.HabitCompletions.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.HabitId == item.HabitId && c.ClientRequestId == item.ClientRequestId);
                if (existing != null)
                {
                    return Success(item.ClientRequestId, existing.Id);
                }

                var habit = await _context.Habits.FindAsync(item.HabitId);
                if (habit == null || habit.UserId != userId)
                {
                    return Failure(item.ClientRequestId, "Alışkanlık bulunamadı.");
                }

                var completionUtc = DateTime.SpecifyKind(item.CompletionDate, DateTimeKind.Utc);
                if (completionUtc.Date < habit.CreatedAt.Date)
                {
                    return Failure(item.ClientRequestId, "Tamamlama tarihi alışkanlığın oluşturulma tarihinden önce olamaz.");
                }
                if (completionUtc > DateTime.UtcNow)
                {
                    return Failure(item.ClientRequestId, "Tamamlama tarihi gelecekte olamaz.");
                }

                var tz = TimeZones.Resolve(user?.TimeZoneId);
                var isOnTime = HabitSchedule.IsWithinTargetTime(habit, completionUtc, tz);
                var snapshot = await _progressService.GetCompletionSnapshotAsync(
                    habit, completionUtc, item.Amount, user?.TimeZoneId);

                var streakKept = snapshot.GoalJustReached && snapshot.PreviousPeriodGoalMet;
                var xpEarned = _xpService.CalculateCompletionXp(habit, item.Amount, snapshot.TotalBeforeInPeriod, streakKept, isOnTime);
                var petStreakBonus = streakKept ? _xpService.GetStreakKeepBonus() : 0;

                var entry = _context.HabitCompletions.Add(new HabitCompletion
                {
                    HabitId = habit.Id,
                    CompletionDate = completionUtc,
                    Amount = item.Amount,
                    XpEarned = xpEarned,
                    PetStreakBonusXp = petStreakBonus,
                    IsOnTime = isOnTime,
                    ClientRequestId = item.ClientRequestId
                });

                _context.Entry(habit).Property(h => h.ConcurrencyToken).IsModified = true;

                if (user != null)
                {
                    user.TotalXp += xpEarned;
                    await _userManager.UpdateAsync(user);
                }

                await _context.SaveChangesAsync();

                Flower? flower = null;
                if (HabitCategories.IsWater(habit.Category) && item.Amount > 0)
                {
                    flower = await _flowerService.AddWaterAsync(userId, item.Amount);
                }
                if (HabitCategories.IsFocus(habit.Category) && item.Amount > 0)
                {
                    await _petGrowthService.AddFocusXpAsync(userId, item.Amount);
                }
                if (petStreakBonus > 0)
                {
                    await _petGrowthService.AddStreakBonusXpAsync(userId, petStreakBonus);
                }

                await _badgeService.EvaluateAfterCompletionAsync(userId, habit, snapshot, flower);

                if (snapshot.GoalJustReached)
                {
                    await _notificationService.TryEnqueueAsync(
                        userId, NotificationTypes.GoalReached, "Hedef tamamlandı",
                        MotivationMessages.GoalReached(habit.Name), habit.Id,
                        $"goal:{habit.Id}:{snapshot.PeriodStartLocal:yyyy-MM-dd}");
                }

                await transaction.CommitAsync();
                return Success(item.ClientRequestId, entry.Entity.Id);
            }
            catch (Exception ex)
            {
                return Failure(item.ClientRequestId, ex.Message);
            }
        });
    }

    private async Task<BatchItemResultDto> ProcessBookReadingLogAsync(
        string userId, User? user, BatchBookReadingLogItemDto item)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync();

                var existing = await _context.BookReadingLogs.AsNoTracking()
                    .FirstOrDefaultAsync(l => l.BookId == item.BookId && l.ClientRequestId == item.ClientRequestId);
                if (existing != null)
                {
                    return Success(item.ClientRequestId, existing.Id);
                }

                var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == item.BookId && b.UserId == userId);
                if (book == null)
                {
                    return Failure(item.ClientRequestId, "Kitap bulunamadı.");
                }
                if (item.ReadDate > DateTime.UtcNow)
                {
                    return Failure(item.ClientRequestId, "Okuma tarihi gelecekte olamaz.");
                }

                var dto = new LogReadingDto
                {
                    ReadDate = item.ReadDate,
                    Amount = item.Amount,
                    PageReachedAt = item.PageReachedAt
                };

                var addResult = await _bookService.AddReadingLogAsync(book, dto, user?.TimeZoneId);
                addResult.Log.ClientRequestId = item.ClientRequestId;
                await _context.SaveChangesAsync();

                if (user != null && addResult.XpEarned != 0)
                {
                    user.TotalXp += addResult.XpEarned;
                    await _userManager.UpdateAsync(user);
                }

                await _badgeService.EvaluateAfterBookLogAsync(userId, addResult.StreakAfterDays);

                if (addResult.GoalJustReachedInPeriod)
                {
                    var periodKey = addResult.PeriodStartLocal.ToString("yyyy-MM-dd");
                    await _notificationService.TryEnqueueAsync(
                        userId, NotificationTypes.BookGoalReached, "Okuma hedefi tamamlandı",
                        MotivationMessages.BookGoalReached(book.Title), habitId: null,
                        dedupKey: $"bookgoal:{book.Id}:{periodKey}");
                }
                if (addResult.BookJustCompleted)
                {
                    await _notificationService.TryEnqueueAsync(
                        userId, NotificationTypes.BookCompleted, "Kitap tamamlandı",
                        MotivationMessages.BookCompleted(book.Title), habitId: null,
                        dedupKey: $"bookcompleted:{book.Id}");
                }

                await transaction.CommitAsync();
                return Success(item.ClientRequestId, addResult.Log.Id);
            }
            catch (Exception ex)
            {
                return Failure(item.ClientRequestId, ex.Message);
            }
        });
    }

    private static BatchItemResultDto Success(string clientRequestId, int id) => new()
    {
        ClientRequestId = clientRequestId,
        Success = true,
        CreatedId = id
    };

    private static BatchItemResultDto Failure(string clientRequestId, string error) => new()
    {
        ClientRequestId = clientRequestId,
        Success = false,
        Error = error
    };
}