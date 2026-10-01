using Microsoft.Extensions.Caching.Memory;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Brute-force protection via account lockout (Req 23.8).
/// 10 consecutive failed attempts within 30 minutes → account locked for 30 minutes.
/// Uses IMemoryCache for storage with sliding expiration.
/// </summary>
public class AccountLockoutService : IAccountLockoutService
{
    private const int MaxFailedAttempts = 10;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AttemptWindowDuration = TimeSpan.FromMinutes(30);

    private const string AttemptsKeyPrefix = "lockout:attempts:";
    private const string LockedKeyPrefix = "lockout:locked:";

    private readonly IMemoryCache _cache;

    public AccountLockoutService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool IsLockedOut(string email)
    {
        var key = LockedKeyPrefix + NormalizeEmail(email);
        return _cache.TryGetValue(key, out _);
    }

    public void RecordFailedAttempt(string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        var attemptsKey = AttemptsKeyPrefix + normalizedEmail;

        var attempts = _cache.GetOrCreate(attemptsKey, entry =>
        {
            entry.SlidingExpiration = AttemptWindowDuration;
            return 0;
        });

        attempts++;

        if (attempts >= MaxFailedAttempts)
        {
            // Lock the account
            var lockedKey = LockedKeyPrefix + normalizedEmail;
            _cache.Set(lockedKey, true, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = LockoutDuration
            });

            // Remove the attempts counter — it resets after lockout expires
            _cache.Remove(attemptsKey);
        }
        else
        {
            // Update the counter with sliding expiration
            _cache.Set(attemptsKey, attempts, new MemoryCacheEntryOptions
            {
                SlidingExpiration = AttemptWindowDuration
            });
        }
    }

    public void ResetAttempts(string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        _cache.Remove(AttemptsKeyPrefix + normalizedEmail);
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
