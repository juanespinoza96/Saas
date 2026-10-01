using System.Collections.Concurrent;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Middleware que inyecta headers X-RateLimit-* en respuestas exitosas
/// de tráfico autenticado. Los headers informan al cliente sobre el estado
/// actual de su presupuesto de peticiones.
///
/// Headers añadidos (solo tráfico autenticado):
/// - X-RateLimit-Limit: Límite total de la ventana (300)
/// - X-RateLimit-Remaining: Peticiones restantes en la ventana actual (0-299)
/// - X-RateLimit-Reset: Timestamp Unix UTC del reinicio de la ventana
///
/// Requirement 2.5: Headers X-RateLimit-* solo para autenticados.
///
/// Usa Response.OnStarting() para garantizar que los headers se escriben
/// ANTES de que el response body comience a enviarse, evitando la condición
/// donde Response.HasStarted == true impide la escritura de headers.
///
/// Estrategia dual dentro del callback:
/// 1. Primario: Lee los Items seteados por el GlobalLimiter en Program.cs
/// 2. Fallback: Si los Items no están disponibles, recalcula directamente desde
///    el token JWT del header Authorization (auto-contenido).
/// </summary>
public class RateLimitHeadersMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Diccionario concurrente que rastrea el conteo de peticiones por clave de partición.
    /// Clave: partitionKey (IP:UserId), Valor: (count, windowStartUnix)
    /// Se resetea automáticamente cuando la ventana de 1 minuto expira.
    /// </summary>
    internal static readonly ConcurrentDictionary<string, (int Count, long WindowStartTicks)> RequestCounters = new();

    /// <summary>
    /// Limpia todos los contadores de peticiones. Usado en tests de integración
    /// para aislar el estado entre ejecuciones y evitar que contadores acumulados
    /// de tests previos causen falsos 429.
    /// </summary>
    public static void ResetCounters() => RequestCounters.Clear();

    /// <summary>
    /// Duración de la ventana fija en ticks (1 minuto).
    /// </summary>
    private static readonly long WindowDurationTicks = TimeSpan.FromMinutes(1).Ticks;

    /// <summary>
    /// Límite de peticiones para tráfico autenticado.
    /// </summary>
    private const int AuthenticatedPermitLimit = 300;

    public RateLimitHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Registrar callback OnStarting ANTES de llamar _next().
        // OnStarting se ejecuta justo antes de que ASP.NET Core envíe los headers al cliente,
        // garantizando que los headers X-RateLimit-* se escriben incluso si el body ya empezó
        // durante la ejecución del pipeline (Response.HasStarted == true post-_next).
        context.Response.OnStarting(() =>
        {
            // No agregar headers en respuestas 429 (ya tienen Retry-After, evitar duplicación)
            if (context.Response.StatusCode == StatusCodes.Status429TooManyRequests)
                return Task.CompletedTask;

            // Estrategia primaria: leer Items seteados por el GlobalLimiter en Program.cs
            if (context.Items.TryGetValue("RateLimit_IsAuthenticated", out var isAuthObj)
                && isAuthObj is true
                && context.Items.TryGetValue("RateLimit_UserId", out var userIdObj)
                && userIdObj is string userId
                && !string.IsNullOrEmpty(userId))
            {
                var limit = context.Items.TryGetValue("RateLimit_Limit", out var limitObj) && limitObj is int l
                    ? l
                    : AuthenticatedPermitLimit;

                var remaining = context.Items.TryGetValue("RateLimit_Remaining", out var remainingObj) && remainingObj is int r
                    ? r
                    : 0;

                var reset = context.Items.TryGetValue("RateLimit_Reset", out var resetObj) && resetObj is long ts
                    ? ts
                    : DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();

                context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
                context.Response.Headers["X-RateLimit-Reset"] = reset.ToString();
                return Task.CompletedTask;
            }

            // Estrategia fallback: verificar directamente si hay un usuario autenticado
            // (resuelve el caso donde el GlobalLimiter no setea los Items correctamente)
            var sub = context.User?.FindFirst("sub")?.Value
                   ?? context.User?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

            if (!string.IsNullOrEmpty(sub))
            {
                var ip = RateLimitHelpers.ObtenerIpCliente(context);
                var partitionKey = $"{ip}:{sub}";
                var (remaining2, resetTimestamp2) = GetCurrentCounters(partitionKey, AuthenticatedPermitLimit);

                context.Response.Headers["X-RateLimit-Limit"] = AuthenticatedPermitLimit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = remaining2.ToString();
                context.Response.Headers["X-RateLimit-Reset"] = resetTimestamp2.ToString();
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }

    /// <summary>
    /// Calcula e incrementa el contador de peticiones para una clave de partición,
    /// retornando el número de peticiones restantes y el timestamp de reset.
    /// Se utiliza desde el Global Limiter en Program.cs para almacenar la metadata.
    /// </summary>
    /// <param name="partitionKey">Clave de partición (IP:UserId).</param>
    /// <param name="permitLimit">Límite total permitido en la ventana.</param>
    /// <returns>Tupla con (remaining, resetUnixTimestamp).</returns>
    public static (int Remaining, long ResetTimestamp) TrackRequest(string partitionKey, int permitLimit)
    {
        var now = DateTimeOffset.UtcNow;
        var nowTicks = now.Ticks;

        var entry = RequestCounters.AddOrUpdate(
            partitionKey,
            // Si no existe, crear nueva entrada con count = 1
            _ => (Count: 1, WindowStartTicks: nowTicks),
            // Si existe, verificar si la ventana expiró
            (_, existing) =>
            {
                var elapsed = nowTicks - existing.WindowStartTicks;
                if (elapsed >= WindowDurationTicks)
                {
                    // Ventana expirada → reiniciar contador
                    return (Count: 1, WindowStartTicks: nowTicks);
                }
                // Misma ventana → incrementar contador
                return (Count: existing.Count + 1, WindowStartTicks: existing.WindowStartTicks);
            });

        // Calcular remaining: límite menos peticiones en la ventana actual
        var remaining = Math.Max(0, permitLimit - entry.Count);

        // Calcular reset: inicio de ventana + 1 minuto
        var windowStart = new DateTimeOffset(entry.WindowStartTicks, TimeSpan.Zero);
        var resetTimestamp = windowStart.AddMinutes(1).ToUnixTimeSeconds();

        return (remaining, resetTimestamp);
    }

    /// <summary>
    /// Consulta los contadores actuales sin incrementar (lectura pasiva).
    /// Se usa en el fallback cuando el GlobalLimiter ya incrementó el contador
    /// pero no seteo los Items.
    /// </summary>
    private static (int Remaining, long ResetTimestamp) GetCurrentCounters(string partitionKey, int permitLimit)
    {
        var now = DateTimeOffset.UtcNow;
        var nowTicks = now.Ticks;

        if (RequestCounters.TryGetValue(partitionKey, out var existing))
        {
            var elapsed = nowTicks - existing.WindowStartTicks;
            if (elapsed < WindowDurationTicks)
            {
                var remaining = Math.Max(0, permitLimit - existing.Count);
                var windowStart = new DateTimeOffset(existing.WindowStartTicks, TimeSpan.Zero);
                var resetTimestamp = windowStart.AddMinutes(1).ToUnixTimeSeconds();
                return (remaining, resetTimestamp);
            }
        }

        // Sin datos previos o ventana expirada: retornar valores frescos
        return (permitLimit - 1, DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds());
    }
}
