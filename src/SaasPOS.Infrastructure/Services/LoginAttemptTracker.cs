using Microsoft.Extensions.Caching.Memory;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Rastrea intentos fallidos de login por combinación IP + email usando IMemoryCache.
/// Ventana fija de 30 minutos con máximo 3 intentos (Req 4.5, 4.6, 4.7).
/// Cada entrada expira automáticamente después de 30 minutos (absoluta).
/// </summary>
public class LoginAttemptTracker : ILoginAttemptTracker
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(30);
    private const string KeyPrefix = "login_attempts:";

    private readonly IMemoryCache _cache;

    public LoginAttemptTracker(IMemoryCache cache)
    {
        _cache = cache;
    }

    public int RecordFailedAttempt(string ip, string email)
    {
        var key = BuildKey(ip, email);

        var attempts = _cache.GetOrCreate(key, entry =>
        {
            // Usar expiración absoluta para simular ventana fija de 30 minutos
            entry.AbsoluteExpirationRelativeToNow = WindowDuration;
            return 0;
        });

        attempts++;

        // Actualizar el contador manteniendo la misma expiración
        // No usamos sliding — la ventana es fija desde el primer intento
        _cache.Set(key, attempts, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = WindowDuration
        });

        return attempts;
    }

    public int GetCurrentAttempts(string ip, string email)
    {
        var key = BuildKey(ip, email);
        return _cache.TryGetValue(key, out int attempts) ? attempts : 0;
    }

    public void ResetAttempts(string ip, string email)
    {
        var key = BuildKey(ip, email);
        _cache.Remove(key);
    }

    private static string BuildKey(string ip, string email)
    {
        // Normalizar email a minúsculas, consistente con la partición de LoginPolicy
        return $"{KeyPrefix}{ip}:{email.Trim().ToLowerInvariant()}";
    }
}
