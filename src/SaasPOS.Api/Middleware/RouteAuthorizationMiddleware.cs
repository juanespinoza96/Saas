using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Middleware that enforces route-prefix authorization (Req 16.4):
/// - /api/admin/** requires role = SuperAdmin; a POS user gets HTTP 403.
/// - /api/tenants/** requires one of the tenant roles (Cajero, Gerente, Dueño, Supervisor, Bodeguero).
/// This runs AFTER authentication and JTI validation so that claims are populated.
/// </summary>
public class RouteAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RouteAuthorizationMiddleware> _logger;

    private static readonly HashSet<string> TenantRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cajero", "Gerente", "Dueño", "Supervisor", "Bodeguero"
    };

    public RouteAuthorizationMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, ILogger<RouteAuthorizationMiddleware> logger)
    {
        _next = next;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        // Skip auth enforcement for anonymous endpoints (login, password recovery)
        if (path.StartsWithSegments("/api/tenants/auth", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/admin/auth", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            // Must be authenticated
            if (context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "No autorizado.", code = "UNAUTHORIZED" });
                return;
            }

            // Must have SuperAdmin role
            var role = GetUserRole(context);
            if (!string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                LogCrossTenantAccess(context, role, path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Acceso denegado. Se requiere rol SuperAdmin.", code = "FORBIDDEN" });
                return;
            }
        }
        else if (path.StartsWithSegments("/api/tenants", StringComparison.OrdinalIgnoreCase))
        {
            // Must be authenticated
            if (context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "No autorizado.", code = "UNAUTHORIZED" });
                return;
            }

            // Must have a valid tenant role
            var role = GetUserRole(context);
            if (role == null || !TenantRoles.Contains(role))
            {
                LogCrossTenantAccess(context, role, path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Acceso denegado. Rol no autorizado para rutas de tenant.", code = "FORBIDDEN" });
                return;
            }

            // Verificar si el comercio está suspendido (Req 1.x)
            if (await IsComercioSuspendidoAsync(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Su comercio se encuentra suspendido. Contacte al administrador de la plataforma.", code = "COMMERCE_SUSPENDED" });
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Verifica si el comercio del usuario autenticado está suspendido.
    /// Cachea el resultado en context.Items para evitar consultas múltiples por request.
    /// </summary>
    private async Task<bool> IsComercioSuspendidoAsync(HttpContext context)
    {
        const string cacheKey = "__ComercioSuspendido";

        // Si ya se consultó en este request, retornar resultado cacheado
        if (context.Items.TryGetValue(cacheKey, out var cached))
            return (bool)cached!;

        var comercioIdClaim = context.User.FindFirstValue("comercio_id");
        if (string.IsNullOrEmpty(comercioIdClaim) || !int.TryParse(comercioIdClaim, out var comercioId))
        {
            context.Items[cacheKey] = false;
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var estado = await dbContext.Comercios
            .IgnoreQueryFilters()
            .Where(c => c.Id == comercioId)
            .Select(c => c.Estado)
            .FirstOrDefaultAsync();

        var isSuspendido = string.Equals(estado, "Suspendido", StringComparison.OrdinalIgnoreCase);
        context.Items[cacheKey] = isSuspendido;

        return isSuspendido;
    }

    private static string? GetUserRole(HttpContext context)
    {
        // Check custom "role" claim first (our JWT uses "role" as RoleClaimType)
        return context.User.FindFirstValue("role")
               ?? context.User.FindFirstValue(ClaimTypes.Role);
    }

    /// <summary>
    /// Logs an unauthorized access attempt to the security audit trail (Req 3.4, 3.7).
    /// Fire-and-forget to avoid impacting response latency.
    /// Utiliza ISecurityAuditService para auditoría de eventos de seguridad.
    /// </summary>
    private void LogCrossTenantAccess(HttpContext context, string? role, PathString attemptedRoute)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var securityAuditService = scope.ServiceProvider.GetRequiredService<ISecurityAuditService>();
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Extraer ComercioId y UsuarioId del atacante si están disponibles
                var comercioIdClaim = context.User?.FindFirstValue("comercio_id");
                var userIdClaim = context.User?.FindFirstValue("sub")
                    ?? context.User?.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

                int comercioId = 0;
                int? usuarioId = null;

                if (int.TryParse(comercioIdClaim, out var parsedComercioId))
                    comercioId = parsedComercioId;
                if (int.TryParse(userIdClaim, out var parsedUserId))
                    usuarioId = parsedUserId;

                await securityAuditService.LogAccessViolationAsync(
                    comercioId: comercioId,
                    usuarioId: usuarioId,
                    ipAddress: ipAddress,
                    path: attemptedRoute.ToString(),
                    reason: $"Rol no autorizado: {role ?? "none"}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log cross-tenant access audit for {Path}", attemptedRoute);
            }
        });
    }
}
