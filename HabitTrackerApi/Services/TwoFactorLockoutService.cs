using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public class TwoFactorLockoutService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _context;

    public TwoFactorLockoutService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsLockedOutAsync(string userId, CancellationToken cancellationToken = default)
    {
        var attempt = await _context.TwoFactorAttempts
            .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

        return attempt?.LockedUntil != null && attempt.LockedUntil > DateTime.UtcNow;
    }

    public async Task RecordFailureAsync(string userId, CancellationToken cancellationToken = default)
    {
        var attempt = await _context.TwoFactorAttempts
            .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

        if (attempt == null)
        {
            attempt = new TwoFactorAttempt { UserId = userId, FailedCount = 0 };
            _context.TwoFactorAttempts.Add(attempt);
        }

        attempt.FailedCount++;
        attempt.UpdatedAt = DateTime.UtcNow;

        if (attempt.FailedCount >= MaxFailedAttempts)
        {
            attempt.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var attempt = await _context.TwoFactorAttempts
            .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

        if (attempt == null)
        {
            return;
        }

        attempt.FailedCount = 0;
        attempt.LockedUntil = null;
        attempt.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}