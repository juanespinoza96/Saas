using SaasPOS.Api.Services;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Middleware que gestiona el contexto de tenant:
/// - Para rutas /api/admin/ reemplaza el ITenantContext con AdminTenantContext (bypass de filtros).
/// - Para rutas /api/tenants/ actúa como verificación de respaldo de que el Comercio no esté suspendido.
///
/// FUENTE ÚNICA DE VERDAD de la verificación de suspensión: RouteAuthorizationMiddleware.
/// Ese middleware corre ANTES que este en el pipeline, consulta la tabla Comercios una sola vez
/// (IgnoreQueryFilters) y DEJA el resultado booleano cacheado en context.Items["__ComercioSuspendido"].
/// Además, para un comercio suspendido ya responde 403 { error, code = COMMERCE_SUSPENDED } y
/// corta la cadena ANTES de llegar aquí. Por eso este middleware NO vuelve a consultar la base de
/// datos: solo REUTILIZA el valor cacheado como salvaguarda defensiva, evitando la doble consulta
/// redundante que existía antes.
/// </summary>
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    // Clave compartida con RouteAuthorizationMiddleware, que es quien puebla este valor.
    private const string ComercioSuspendidoCacheKey = "__ComercioSuspendido";

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
            // Reutilizar el resultado de suspensión calculado por RouteAuthorizationMiddleware.
            // NO se consulta la base de datos aquí (se eliminó la segunda consulta redundante).
            if (context.Items.TryGetValue(ComercioSuspendidoCacheKey, out var cached) && cached is bool isSuspendido)
            {
                if (isSuspendido)
                {
                    // Salvaguarda defensiva: en el flujo normal RouteAuthorizationMiddleware ya habría
                    // respondido 403 antes de llegar aquí. Se mantiene el mismo formato de error
                    // ({ error, code = COMMERCE_SUSPENDED }) para que la respuesta sea coherente con
                    // la de RouteAuthorizationMiddleware. El frontend no depende de un formato concreto
                    // de este 403 (lo trata de forma genérica en src/SaasPOS.Web/src/lib/api.ts).
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Su comercio se encuentra suspendido. Contacte al administrador de la plataforma.",
                        code = "COMMERCE_SUSPENDED"
                    });
                    return;
                }
                // Comercio activo (valor false): continuar normalmente.
            }
            // Si la clave NO está presente, se continúa sin bloquear (estrategia (a)).
            // Es seguro porque la única ruta /api/tenants/** que NO puebla la clave es
            // /api/tenants/auth (endpoints anónimos de login/recuperación), que RouteAuthorizationMiddleware
            // deja pasar temprano SIN verificar suspensión y que no deben bloquearse. Toda otra ruta
            // /api/tenants/** no-/auth pasa obligatoriamente por IsComercioSuspendidoAsync, que SIEMPRE
            // puebla la clave (incluso en false cuando no hay comercio_id válido).
        }

        await _next(context);
    }
}
