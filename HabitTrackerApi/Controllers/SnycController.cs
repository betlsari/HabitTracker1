using Data;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using System.Security.Claims;
using Asp.Versioning;


namespace Controllers;


[ApiController]
[ApiVersion("1.0")]
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
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        AppDbContext context,
        UserManager<User> userManager,
        XpService xpService,
        HabitProgressService progressService,
        FlowerService flowerService,
        PetGrowthService petGrowthService,
        BadgeService badgeService,
        NotificationService notificationService,
        BookService bookService,
        ILogger<SyncController> logger)
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
        _logger = logger;
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

        var result = new BatchSyncResultDto();

        foreach (var item in request.HabitCompletions)
        {
            result.HabitCompletions.Add(await ProcessHabitCompletionAsync(userId, item));
        }

        foreach (var item in request.BookReadingLogs)
        {
            result.BookReadingLogs.Add(await ProcessBookReadingLogAsync(userId, item));
        }

        return Ok(result);
    }

    // DÜZELTİLDİ (🔴 stale entity): Önceden 'user', dış SyncBatch metodunda,
    // _context.ChangeTracker.Clear() çağrılmadan ÖNCE bir kez çekilip her
    // Process*Async çağrısına parametre olarak geçiriliyordu. Ancak her
    // Process*Async kendi transaction'ı içinde _context.ChangeTracker.Clear()
    // çağırıyor — bu, EF Core'un o DbContext örneğinde takip ettiği TÜM
    // entity'leri (dışarıdan geçirilen 'user' dahil) "detached" durumuna
    // düşürüyor. Sonrasında 'user.TotalXp += xpEarned; _userManager.
    // UpdateAsync(user)' çağrıldığında, UpdateAsync detached bir entity'yi
    // günceller; bu EF Core'un concurrency/tracking mekanizmasıyla tutarsız
    // davranışlara (ör. ConcurrencyStamp uyuşmazlığı nedeniyle sessiz
    // başarısızlık ve "Success: false" dönmesi) yol açabiliyordu. Artık
    // HabitCompletionsController/BooksController ile aynı desende: 'user'
    // ChangeTracker.Clear()'DAN SONRA, her Process*Async içinde taze olarak
    // çekiliyor.
    private async Task<BatchItemResultDto> ProcessHabitCompletionAsync(
        string userId, BatchHabitCompletionItemDto item)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync();

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

                var user = await _userManager.FindByIdAsync(userId);

                var completionUtc = DateTime.SpecifyKind(item.CompletionDate, DateTimeKind.Utc);
                if (completionUtc.Date < habit.CreatedAt.Date)
                {
                    return Failure(item.ClientRequestId, "Tamamlama tarihi alışkanlığın oluşturulma tarihinden önce olamaz.");
                }
                if (completionUtc > DateTime.UtcNow)
                {
                    return Failure(item.ClientRequestId, "Tamamlama tarihi gelecekte olamaz.");
                }
                // DÜZELTİLDİ (🔴 tutarsızlık): Tekil HabitCompletionsController
                // uç noktası CreateCompletionDto.Validate() üzerinden
                // MaxPastDays (10 yıl) sınırını uyguluyordu; bu batch akışında
                // aynı kontrol hiç yapılmıyordu. Bu sayede batch endpoint'i
                // üzerinden tekil uçtan geçmeyecek, aşırı eski tarihli (ör.
                // yüzlerce yıl önceki) sahte tamamlama kayıtları oluşturulabiliyordu.
                if (completionUtc < DateTime.UtcNow.AddDays(-CreateCompletionDto.MaxPastDays))
                {
                    return Failure(item.ClientRequestId,
                        $"Tamamlama tarihi en fazla {CreateCompletionDto.MaxPastDays} gün öncesine ait olabilir.");
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
                    var updateResult = await _userManager.UpdateAsync(user);
                    updateResult.EnsureSucceeded(_logger, "sync-habit-completion-xp", userId);
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
                // DÜZELTİLDİ: Beklenmeyen hatalar artık loglanıyor. Önceden
                // hata sessizce yutulup sadece ex.Message istemciye
                // dönüyordu; sunucu tarafında hiçbir iz kalmıyordu ve teşhis
                // koymak zorlaşıyordu.
                _logger.LogError(ex,
                    "Batch habit completion senkronizasyonu sırasında hata oluştu. UserId={UserId} HabitId={HabitId} ClientRequestId={ClientRequestId}",
                    userId, item.HabitId, item.ClientRequestId);
                return Failure(item.ClientRequestId, ex.Message);
            }
        });
    }

    private async Task<BatchItemResultDto> ProcessBookReadingLogAsync(
        string userId, BatchBookReadingLogItemDto item)
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

                var user = await _userManager.FindByIdAsync(userId);

                if (item.ReadDate > DateTime.UtcNow)
                {
                    return Failure(item.ClientRequestId, "Okuma tarihi gelecekte olamaz.");
                }

                // DÜZELTİLDİ (🔴 tutarsızlık): Habit tarafında hem tekil hem
                // artık batch uçta "tamamlama tarihi oluşturulma tarihinden
                // önce olamaz" kontrolü varken, Book/BookReadingLog tarafında
                // bu kontrol ne tekil (BooksController.LogReading) ne de
                // batch akışında hiç yapılmıyordu. Kitap oluşturulmadan
                // önceki bir tarihe okuma kaydı eklenebiliyordu.
                var readDateUtc = DateTime.SpecifyKind(item.ReadDate, DateTimeKind.Utc);
                if (readDateUtc.Date < book.CreatedAt.Date)
                {
                    return Failure(item.ClientRequestId, "Okuma tarihi, kitabın oluşturulma tarihinden önce olamaz.");
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
                    var updateResult = await _userManager.UpdateAsync(user);
                    updateResult.EnsureSucceeded(_logger, "sync-book-reading-log-xp", userId);
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
                // DÜZELTİLDİ: bkz. ProcessHabitCompletionAsync üzerindeki
                // aynı düzeltme açıklaması.
                _logger.LogError(ex,
                    "Batch book reading log senkronizasyonu sırasında hata oluştu. UserId={UserId} BookId={BookId} ClientRequestId={ClientRequestId}",
                    userId, item.BookId, item.ClientRequestId);
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