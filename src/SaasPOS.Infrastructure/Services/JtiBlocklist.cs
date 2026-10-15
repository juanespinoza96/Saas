using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Blocklist de JTI. Los cortes masivos por usuario/comercio siguen 100% en IMemoryCache
/// (estado en memoria con auto-expiración). La revocación individual de un JTI se persiste
/// en PostgreSQL (tabla JtiRevocados) con una caché de corta vida (30 s) como aceleradora,
/// de modo que la revocación sobreviva a los reinicios del proceso.
///
/// Se registra como Singleton para que el estado en memoria persista entre requests. Como
/// AppDbContext es un servicio Scoped, no puede inyectarse directamente en un Singleton; por
/// eso se inyecta IServiceScopeFactory (mismo patrón que los BackgroundService existentes como
/// PasswordRecoveryCleanupBackgroundService), que permite abrir un scope efímero por operación
/// individual y resolver un AppDbContext aislado dentro de él.
/// </summary>
public class JtiBlocklist : IJtiBlocklist
{
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JtiBlocklist> _logger;
    private const string BlockedPrefix = "jti:blocked:";
    private const string UserBlockPrefix = "jti:user:";
    private const string ComercioBlockPrefix = "jti:comercio:";

    // TTL de la caché aceleradora de la revocación individual. Replica el TTL de 30 s que
    // JtiValidationMiddleware aplica a Usuario.Activo, para acotar la ventana de inconsistencia
    // entre la BD y la caché en memoria.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public JtiBlocklist(IMemoryCache cache, IServiceScopeFactory scopeFactory, ILogger<JtiBlocklist> logger)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void AddToBlocklist(string jti, DateTime expiration)
    {
        // Entrada vacía/nula → no se persiste nada.
        if (string.IsNullOrEmpty(jti))
            return;

        // Token ya expirado → no se persiste (preserva el comportamiento previo, Req 8.3).
        if (expiration <= DateTime.UtcNow)
            return;

        // Upsert IDEMPOTENTE en BD: el índice único ix_jti_revocados_jti evita duplicados.
        // El scope efímero se libera en el finally (la tupla ValueTuple no es IDisposable).
        var (scope, db) = CrearScopeConDbContext();
        try
        {
            // Solo insertamos si no existe ya una fila con ese Jti.
            if (!db.JtiRevocados.Any(j => j.Jti == jti))
            {
                db.JtiRevocados.Add(new JtiRevocado
                {
                    Jti = jti,
                    Expiracion = expiration,
                    FechaRevocacion = DateTime.UtcNow
                });
                db.SaveChanges();
            }
        }
        catch (DbUpdateException)
        {
            // Otra ejecución concurrente insertó el mismo Jti entre el Any() y el SaveChanges():
            // la violación del índice único se trata como ÉXITO (el token ya quedó revocado).
            _logger.LogDebug(
                "AddToBlocklist: inserción concurrente del jti ya revocado detectada (violación de índice único tratada como éxito).");
        }
        finally
        {
            scope.Dispose();
        }

        // Poblado inmediato de la caché para lecturas consistentes en el mismo nodo.
        // El vencimiento se acota a la Expiracion real del token (Req 8.4).
        var absoluteExpiration = expiration < DateTime.UtcNow.Add(CacheTtl)
            ? expiration
            : DateTime.UtcNow.Add(CacheTtl);

        _cache.Set($"{BlockedPrefix}{jti}", true, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = absoluteExpiration
        });
    }

    public bool IsBlocked(string jti)
    {
        // Entrada vacía/nula → no bloqueado, sin tocar BD.
        if (string.IsNullOrEmpty(jti))
            return false;

        // Acierto de caché: devolver el valor cacheado (puede ser true o false).
        if (_cache.TryGetValue($"{BlockedPrefix}{jti}", out bool cacheado))
            return cacheado;

        // Fallo de caché → consultar BD. Política FAIL-CLOSED: si la BD falla, la excepción
        // se PROPAGA (no se devuelve false silenciosamente); el finally libera el scope.
        var (scope, db) = CrearScopeConDbContext();
        JtiRevocado? fila;
        try
        {
            fila = db.JtiRevocados.FirstOrDefault(j => j.Jti == jti);
        }
        finally
        {
            scope.Dispose();
        }

        // Auto-expiración lógica: un jti cuya Expiracion ya pasó se trata como NO bloqueado,
        // aunque la fila siga en la tabla hasta la limpieza periódica (Req 8.6).
        var bloqueado = fila != null && fila.Expiracion > DateTime.UtcNow;

        // Poblar la caché: si está bloqueado, acotar el vencimiento a la Expiracion real;
        // si no, basta el TTL fijo de 30 s (Req 8.4, 8.5).
        DateTime absoluteExpiration;
        if (bloqueado)
        {
            var masTtl = DateTime.UtcNow.Add(CacheTtl);
            absoluteExpiration = masTtl < fila!.Expiracion ? masTtl : fila.Expiracion;
        }
        else
        {
            absoluteExpiration = DateTime.UtcNow.Add(CacheTtl);
        }

        _cache.Set($"{BlockedPrefix}{jti}", bloqueado, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = absoluteExpiration
        });

        return bloqueado;
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

    /// <summary>
    /// Abre un scope de servicios efímero y resuelve un AppDbContext aislado dentro de él.
    /// Mecanismo de acceso a BD para las operaciones de revocación INDIVIDUAL (AddToBlocklist/
    /// IsBlocked), necesario porque esta clase es Singleton y AppDbContext es Scoped. Sigue el
    /// mismo patrón que los BackgroundService existentes (p. ej. PasswordRecoveryCleanupBackgroundService).
    ///
    /// El llamador DEBE envolver la invocación en un bloque using para que el scope (y el
    /// AppDbContext que contiene) se liberen al terminar la operación. Los cortes masivos
    /// (BlockAllForUser/BlockAllForComercio) NO usan este helper: permanecen 100% en IMemoryCache.
    /// </summary>
    private (IServiceScope Scope, AppDbContext DbContext) CrearScopeConDbContext()
    {
        var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (scope, dbContext);
    }
}
