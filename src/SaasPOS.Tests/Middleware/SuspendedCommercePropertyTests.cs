// Feature: prueba-gratuita, Property 9: Bloqueo de acceso a comercio suspendido
using System.Security.Claims;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SaasPOS.Api.Middleware;
using SaasPOS.Api.Services;

namespace SaasPOS.Tests.Middleware;

/// <summary>
/// Property 9: Bloqueo de acceso a comercio suspendido
/// Validates: Requirements 4.2
/// For any request a una ruta bajo /api/tenants/ de un Comercio con Estado "Suspendido",
/// el sistema SHALL responder con HTTP 403.
///
/// Nota de diseño: la verificación de suspensión es responsabilidad de
/// RouteAuthorizationMiddleware (fuente única de verdad), que corre ANTES que
/// TenantContextMiddleware y deja el resultado booleano cacheado en
/// context.Items["__ComercioSuspendido"]. TenantContextMiddleware ya NO consulta la
/// base de datos: reutiliza ese valor como salvaguarda. Por eso estas propiedades
/// simulan el valor que RouteAuthorizationMiddleware habría dejado y verifican el
/// comportamiento OBSERVABLE a través de TenantContextMiddleware (403 vs. passthrough),
/// sin acoplar el test a la consulta a la BD.
/// </summary>
public class SuspendedCommercePropertyTests
{
    // Debe coincidir con la clave que comparten RouteAuthorizationMiddleware y TenantContextMiddleware.
    private const string ComercioSuspendidoCacheKey = "__ComercioSuspendido";

    /// <summary>
    /// Crea un HttpContext con los servicios necesarios para el middleware y, opcionalmente,
    /// con el resultado de suspensión ya cacheado tal como lo dejaría RouteAuthorizationMiddleware.
    /// </summary>
    private static HttpContext CreateHttpContext(string path, int comercioId, bool? suspendidoCache)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        // Configurar claims con comercio_id
        var claims = new List<Claim>
        {
            new("comercio_id", comercioId.ToString()),
            new("role", "Gerente")
        };
        var identity = new ClaimsIdentity(claims, "TestScheme");
        context.User = new ClaimsPrincipal(identity);

        // Simular el valor que RouteAuthorizationMiddleware deja en context.Items.
        if (suspendidoCache.HasValue)
        {
            context.Items[ComercioSuspendidoCacheKey] = suspendidoCache.Value;
        }

        // Configurar los servicios requeridos para la rama /api/tenants (no se consulta la BD).
        var services = new ServiceCollection();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = context };
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddScoped<TenantContext>();
        services.AddScoped<TenantContextAccessor>();

        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    private static string RutaTenantAleatoria(int seed)
    {
        var segments = new[]
        {
            "productos", "ventas", "categorias", "clientes", "stock",
            "sucursales", "usuarios", "reportes", "configuracion",
            "facturas", "inventario", "dashboard", "perfil"
        };
        var segment = segments[Math.Abs(seed) % segments.Length];
        return $"/api/tenants/{segment}";
    }

    /// <summary>
    /// Propiedad: Cualquier request a /api/tenants/{segmento} de un comercio marcado como
    /// suspendido (cache = true, tal como lo dejaría RouteAuthorizationMiddleware)
    /// SIEMPRE retorna HTTP 403.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Comercio_Suspendido_Siempre_Retorna_403(int seed)
    {
        var path = RutaTenantAleatoria(seed);
        var comercioId = 100 + Math.Abs(seed % 1000);

        var nextCalled = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // RouteAuthorizationMiddleware habría marcado el comercio como suspendido.
        var httpContext = CreateHttpContext(path, comercioId, suspendidoCache: true);

        middleware.InvokeAsync(httpContext).GetAwaiter().GetResult();

        // El middleware DEBE retornar 403 y NO llamar al siguiente middleware
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        Assert.False(nextCalled, $"El middleware no debería pasar al siguiente handler cuando el comercio está Suspendido (ruta: {path})");
    }

    /// <summary>
    /// Propiedad: Cualquier request a /api/tenants/{segmento} de un comercio marcado como
    /// NO suspendido (cache = false) SIEMPRE pasa al siguiente middleware (no retorna 403).
    /// </summary>
    [Property(MaxTest = 100)]
    public void Comercio_No_Suspendido_Siempre_Pasa_Al_Siguiente_Middleware(int seed)
    {
        var path = RutaTenantAleatoria(seed);
        var comercioId = 2000 + Math.Abs(seed % 1000);

        var nextCalled = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // RouteAuthorizationMiddleware habría marcado el comercio como activo (false).
        var httpContext = CreateHttpContext(path, comercioId, suspendidoCache: false);

        middleware.InvokeAsync(httpContext).GetAwaiter().GetResult();

        // El middleware DEBE pasar al siguiente handler y NO retornar 403
        Assert.True(nextCalled, $"El middleware debería pasar al siguiente handler cuando el comercio está Activo (ruta: {path})");
        Assert.NotEqual(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
    }
}
