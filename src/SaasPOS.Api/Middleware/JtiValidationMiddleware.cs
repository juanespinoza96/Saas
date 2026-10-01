using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Middleware that validates JTI claims against the blocklist.
/// Runs AFTER JWT authentication to reject revoked tokens with 401 (Req 3.4).
/// Also checks user-level and comercio-level session invalidation (Req 2.3).
/// Adicionalmente verifica que el usuario siga activo en la DB (defensa contra pérdida de blocklist en memoria).
/// </summary>
public class JtiValidationMiddleware
{
    private readonly RequestDelegate _next;
    /// <summary>
    /// TTL del cache de estado activo del usuario (30 segundos).
    /// Máximo delay entre desactivación y bloqueo real de requests.
    /// </summary>
    private static readonly TimeSpan UserActiveCacheTtl = TimeSpan.FromSeconds(30);

    public JtiValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, JtiBlocklist jtiBlocklist, AppDbContext dbContext, IMemoryCache memoryCache)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var jti = context.User.FindFirstValue(JwtRegisteredClaimNames.Jti);

            if (!string.IsNullOrEmpty(jti) && jtiBlocklist.IsBlocked(jti))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Token revocado." });
                return;
            }

            // Check user-level block
            var subClaim = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                           ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var iatClaim = context.User.FindFirstValue(JwtRegisteredClaimNames.Iat);

            if (int.TryParse(subClaim, out var userId) && !string.IsNullOrEmpty(iatClaim))
            {
                if (long.TryParse(iatClaim, out var iatUnix))
                {
                    var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;
                    if (jtiBlocklist.IsUserBlocked(userId, issuedAt))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { message = "Sesión invalidada." });
                        return;
                    }
                }

                // Verificación de respaldo: confirmar que el usuario sigue activo en la DB.
                // Usa cache de 30s para evitar una consulta en cada request.
                var cacheKey = $"user_active_{userId}";
                if (!memoryCache.TryGetValue(cacheKey, out bool isActive))
                {
                    isActive = await dbContext.Usuarios
                        .IgnoreQueryFilters()
                        .Where(u => u.Id == userId)
                        .Select(u => u.Activo)
                        .FirstOrDefaultAsync();

                    memoryCache.Set(cacheKey, isActive, UserActiveCacheTtl);
                }

                if (!isActive)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { message = "Usuario desactivado." });
                    return;
                }
            }

            // Check comercio-level block
            var comercioIdClaim = context.User.FindFirstValue("comercio_id");
            if (int.TryParse(comercioIdClaim, out var comercioId) && !string.IsNullOrEmpty(iatClaim))
            {
                if (long.TryParse(iatClaim, out var iatUnix))
                {
                    var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;
                    if (jtiBlocklist.IsComercioBlocked(comercioId, issuedAt))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { message = "Comercio suspendido. Sesiones invalidadas." });
                        return;
                    }
                }
            }
        }

        await _next(context);
    }
}
