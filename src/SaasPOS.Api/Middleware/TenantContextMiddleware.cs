using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.Interfaces;
using SaasPOS.Api.Services;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Middleware que gestiona el contexto de tenant:
/// - Para rutas /api/admin/ reemplaza el ITenantContext con AdminTenantContext (bypass de filtros).
/// - Para rutas /api/tenants/ verifica que el Comercio no esté suspendido; si lo está, responde 403.
/// </summary>
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            // Reemplazar el ITenantContext con AdminTenantContext para acceso cross-tenant
            context.RequestServices.GetRequiredService<TenantContextAccessor>()
                .Override = new AdminTenantContext();
        }
        else if (context.Request.Path.StartsWithSegments("/api/tenants", StringComparison.OrdinalIgnoreCase))
        {
            // Verificar si el comercio está suspendido
            var comercioIdClaim = context.User?.FindFirstValue("comercio_id");
            if (int.TryParse(comercioIdClaim, out var comercioId))
            {
                var dbContext = context.RequestServices.GetRequiredService<AppDbContext>();
                var estado = await dbContext.Comercios
                    .Where(c => c.Id == comercioId)
                    .Select(c => c.Estado)
                    .FirstOrDefaultAsync();

                if (estado == "Suspendido")
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    var body = JsonSerializer.Serialize(new
                    {
                        mensaje = "El comercio se encuentra suspendido. No tiene acceso al sistema."
                    });
                    await context.Response.WriteAsync(body);
                    return;
                }
            }
        }

        await _next(context);
    }
}
