using System.Security.Claims;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Middleware que protege contra accesos cross-tenant (Req 3.7).
/// Verifica que las solicitudes que incluyen un comercioId en la ruta o query string
/// coincidan con el comercioId del token JWT del usuario autenticado.
/// Si se detecta un intento de acceso cross-tenant:
/// - Se registra el intento en LogsAuditoria con categoría "Seguridad"
/// - Se retorna HTTP 403 sin revelar la existencia del recurso
/// 
/// Este middleware actúa como segunda línea de defensa después de los query filters de EF Core.
/// </summary>
public class CrossTenantGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CrossTenantGuardMiddleware> _logger;

    public CrossTenantGuardMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, ILogger<CrossTenantGuardMiddleware> logger)
    {
        _next = next;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Solo aplica a rutas de tenant
        if (!context.Request.Path.StartsWithSegments("/api/tenants", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Solo aplica a usuarios autenticados
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Obtener el comercioId del usuario autenticado
        var userComercioIdClaim = context.User.FindFirstValue("comercio_id");
        if (!int.TryParse(userComercioIdClaim, out var userComercioId))
        {
            await _next(context);
            return;
        }

        // Verificar si hay un comercioId en query parameters que no coincida
        if (context.Request.Query.TryGetValue("comercioId", out var queryComercioId))
        {
            if (int.TryParse(queryComercioId, out var requestedComercioId) && requestedComercioId != userComercioId)
            {
                await LogAndRejectCrossTenantAccess(context, userComercioId, requestedComercioId);
                return;
            }
        }

        // Verificar si hay un comercio_id en query parameters que no coincida
        if (context.Request.Query.TryGetValue("comercio_id", out var queryComercioId2))
        {
            if (int.TryParse(queryComercioId2, out var requestedComercioId) && requestedComercioId != userComercioId)
            {
                await LogAndRejectCrossTenantAccess(context, userComercioId, requestedComercioId);
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Registra el intento de acceso cross-tenant y retorna 403 sin revelar información (Req 3.7).
    /// </summary>
    private async Task LogAndRejectCrossTenantAccess(HttpContext context, int userComercioId, int targetComercioId)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = context.Request.Path.ToString();

        var userIdClaim = context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? usuarioId = int.TryParse(userIdClaim, out var uid) ? uid : null;

        _logger.LogWarning(
            "Cross-tenant access attempt detected: User from comercio {UserComercioId} attempted to access comercio {TargetComercioId} at {Path} from IP {IP}",
            userComercioId, targetComercioId, path, ipAddress);

        // Fire-and-forget audit logging para no impactar latencia
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var securityAuditService = scope.ServiceProvider.GetRequiredService<ISecurityAuditService>();
                await securityAuditService.LogCrossTenantAccessAsync(
                    userComercioId, usuarioId, ipAddress, path, targetComercioId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log cross-tenant access audit");
            }
        });

        // Req 3.7: Retornar 403 sin revelar la existencia del recurso
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Acceso denegado.",
            code = "FORBIDDEN"
        });
    }
}
