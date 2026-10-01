using Microsoft.Extensions.Caching.Memory;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// In-memory JTI blocklist using IMemoryCache.
/// Entries auto-expire when the original JWT would have expired,
/// so the cache stays lean without manual cleanup.
/// Registered as Singleton to persist state across requests.
/// </summary>
public class JtiBlocklist : IJtiBlocklist
{
    private readonly IMemoryCache _cache;
    private const string BlockedPrefix = "jti:blocked:";
    private const string UserBlockPrefix = "jti:user:";
    private const string ComercioBlockPrefix = "jti:comercio:";

    public JtiBlocklist(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void AddToBlocklist(string jti, DateTime expiration)
    {
        var remaining = expiration - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return; // Token already expired, no need to block

        _cache.Set($"{BlockedPrefix}{jti}", true, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = expiration
        });
    }

    public bool IsBlocked(string jti)
    {
        return _cache.TryGetValue($"{BlockedPrefix}{jti}", out _);
    }

    public void BlockAllForUser(int userId)
    {
        // Store a timestamp — any token issued before this time is considered blocked.
        _cache.Set($"{UserBlockPrefix}{userId}", DateTime.UtcNow, new MemoryCacheEntryOptions
        {
            // Keep for 24 hours (longer than max JWT lifetime) to cover all active tokens
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        });
    }

    public void BlockAllForComercio(int comercioId)
    {
        // Store a timestamp — any token issued before this time for this comercio is blocked.
        _cache.Set($"{ComercioBlockPrefix}{comercioId}", DateTime.UtcNow, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        });
    }

    /// <summary>
    /// Checks if all sessions for a given user have been invalidated after a certain time.
    /// </summary>
    public bool IsUserBlocked(int userId, DateTime tokenIssuedAt)
    {
        if (_cache.TryGetValue($"{UserBlockPrefix}{userId}", out DateTime blockedAt))
        {
            return tokenIssuedAt <= blockedAt;
        }
        return false;
    }

    /// <summary>
    /// Checks if all sessions for a given comercio have been invalidated after a certain time.
    /// </summary>
    public bool IsComercioBlocked(int comercioId, DateTime tokenIssuedAt)
    {
        if (_cache.TryGetValue($"{ComercioBlockPrefix}{comercioId}", out DateTime blockedAt))
        {
            return tokenIssuedAt <= blockedAt;
        }
        return false;
    }
}
