// Feature: mejoras-operativas-v2, Property 5: Aislamiento cross-tenant impide acceso entre comercios
using System.Security.Claims;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Api.Middleware;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Tests.Middleware;

/// <summary>
/// Property 5: Aislamiento cross-tenant impide acceso entre comercios
/// 
/// **Validates: Requirements 3.7**
/// 
/// For any par de comercios (A, B) donde A ≠ B, un request autenticado como usuario del
/// comercio B intentando acceder a un recurso del comercio A SHALL recibir HTTP 403
/// sin revelar la existencia del recurso.
/// </summary>
public class CrossTenantIsolationPropertyTests
{
    /// <summary>
    /// Generador de pares de comercioIds distintos (A, B) donde A ≠ B.
    /// Genera IDs positivos en un rango razonable para simular comercios reales.
    /// </summary>
    private static Arbitrary<(int ComercioA, int ComercioB)> DistinctComercioPairArbitrary()
    {
        var gen = from a in Gen.Choose(1, 10000)
                  from b in Gen.Choose(1, 10000).Where(x => x != a)
                  select (a, b);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generador de segmentos de ruta de API de tenant válidos.
    /// </summary>
    private static string[] TenantRouteSegments => new[]
    {
        "productos", "ventas", "categorias", "clientes", "stock",
        "sucursales", "usuarios", "reportes", "configuracion",
        "facturas", "inventario", "dashboard", "comprobantes"
    };

    /// <summary>
    /// Crea un HttpContext simulando un usuario autenticado del comercio B
    /// que intenta acceder a recursos del comercio A vía query parameter.
    /// </summary>
    private static HttpContext CreateCrossTenantHttpContext(
        int userComercioId,
        int targetComercioId,
        string routeSegment,
        string queryParamName = "comercioId")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = $"/api/tenants/{routeSegment}";
        context.Request.QueryString = new QueryString($"?{queryParamName}={targetComercioId}");

        // Configurar claims JWT del usuario autenticado (comercio B)
        var claims = new List<Claim>
        {
            new("comercio_id", userComercioId.ToString()),
            new("sub", "999"),
            new(ClaimTypes.NameIdentifier, "999"),
            new("role", "Gerente")
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        context.User = new ClaimsPrincipal(identity);

        // Configurar respuesta con MemoryStream para poder leer el body
        context.Response.Body = new MemoryStream();

        return context;
    }

    /// <summary>
    /// Crea el middleware con sus dependencias mockeadas.
    /// </summary>
    private static (CrossTenantGuardMiddleware Middleware, Mock<ISecurityAuditService> AuditMock, bool[] NextCalled) CreateMiddleware()
    {
        var nextCalled = new[] { false };
        RequestDelegate next = _ =>
        {
            nextCalled[0] = true;
            return Task.CompletedTask;
        };

        var auditMock = new Mock<ISecurityAuditService>();
        auditMock.Setup(x => x.LogCrossTenantAccessAsync(
            It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        // Configurar IServiceScopeFactory para proveer el ISecurityAuditService
        var serviceScopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(x => x.GetService(typeof(ISecurityAuditService)))
            .Returns(auditMock.Object);
        serviceScopeMock.Setup(x => x.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(x => x.CreateScope()).Returns(serviceScopeMock.Object);

        var loggerMock = new Mock<ILogger<CrossTenantGuardMiddleware>>();

        var middleware = new CrossTenantGuardMiddleware(next, scopeFactoryMock.Object, loggerMock.Object);

        return (middleware, auditMock, nextCalled);
    }

    /// <summary>
    /// Propiedad: Para cualquier par de comercios (A, B) donde A ≠ B,
    /// un request autenticado como usuario del comercio B intentando acceder a un recurso
    /// del comercio A vía query parameter "comercioId" SIEMPRE recibe HTTP 403.
    /// 
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public void CrossTenant_Access_Via_ComercioId_Always_Returns_403(PositiveInt seedA, PositiveInt seedB)
    {
        var comercioA = seedA.Get;
        var comercioB = seedB.Get;

        // Asegurar que A ≠ B
        if (comercioA == comercioB)
            comercioB = comercioA + 1;

        var segmentIndex = Math.Abs(comercioA) % TenantRouteSegments.Length;
        var routeSegment = TenantRouteSegments[segmentIndex];

        var (middleware, _, nextCalled) = CreateMiddleware();

        // Usuario autenticado como comercio B intenta acceder a recurso del comercio A
        var httpContext = CreateCrossTenantHttpContext(
            userComercioId: comercioB,
            targetComercioId: comercioA,
            routeSegment: routeSegment,
            queryParamName: "comercioId");

        middleware.InvokeAsync(httpContext).GetAwaiter().GetResult();

        // DEBE retornar 403
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        // NO debe llamar al siguiente middleware
        Assert.False(nextCalled[0],
            $"El middleware no debe pasar al siguiente handler cuando comercio {comercioB} intenta acceder a comercio {comercioA}");
    }

    /// <summary>
    /// Propiedad: Para cualquier par de comercios (A, B) donde A ≠ B,
    /// un request autenticado como usuario del comercio B intentando acceder a un recurso
    /// del comercio A vía query parameter "comercio_id" SIEMPRE recibe HTTP 403.
    /// 
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public void CrossTenant_Access_Via_Comercio_Id_Underscore_Always_Returns_403(PositiveInt seedA, PositiveInt seedB)
    {
        var comercioA = seedA.Get;
        var comercioB = seedB.Get;

        if (comercioA == comercioB)
            comercioB = comercioA + 1;

        var segmentIndex = Math.Abs(comercioB) % TenantRouteSegments.Length;
        var routeSegment = TenantRouteSegments[segmentIndex];

        var (middleware, _, nextCalled) = CreateMiddleware();

        // Usuario autenticado como comercio B intenta acceder a recurso del comercio A
        // usando el parámetro alternativo "comercio_id"
        var httpContext = CreateCrossTenantHttpContext(
            userComercioId: comercioB,
            targetComercioId: comercioA,
            routeSegment: routeSegment,
            queryParamName: "comercio_id");

        middleware.InvokeAsync(httpContext).GetAwaiter().GetResult();

        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        Assert.False(nextCalled[0],
            $"El middleware no debe pasar al siguiente handler cuando comercio {comercioB} intenta acceder a comercio {comercioA} via comercio_id");
    }

    /// <summary>
    /// Propiedad: Para cualquier par de comercios (A, B) donde A ≠ B,
    /// la respuesta 403 NUNCA revela el comercioId del recurso al que se intentó acceder.
    /// El body solo debe contener un mensaje genérico "Acceso denegado." sin mencionar
    /// el comercio target, cumpliendo Req 3.7 de no revelar la existencia del recurso.
    /// 
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public void CrossTenant_Response_Never_Reveals_Target_ComercioId(PositiveInt seedA, PositiveInt seedB)
    {
        var comercioA = seedA.Get;
        var comercioB = seedB.Get;

        if (comercioA == comercioB)
            comercioB = comercioA + 1;

        var segmentIndex = Math.Abs(comercioA + comercioB) % TenantRouteSegments.Length;
        var routeSegment = TenantRouteSegments[segmentIndex];

        var (middleware, _, _) = CreateMiddleware();

        var httpContext = CreateCrossTenantHttpContext(
            userComercioId: comercioB,
            targetComercioId: comercioA,
            routeSegment: routeSegment);

        middleware.InvokeAsync(httpContext).GetAwaiter().GetResult();

        // Leer el cuerpo de la respuesta
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var responseBody = reader.ReadToEnd();

        // La respuesta NO debe contener el comercioId target
        Assert.DoesNotContain(comercioA.ToString(), responseBody);
        // La respuesta debe contener el mensaje genérico
        Assert.Contains("Acceso denegado.", responseBody);
        // La respuesta debe contener el código FORBIDDEN
        Assert.Contains("FORBIDDEN", responseBody);
    }

    /// <summary>
    /// Propiedad: Cuando el usuario accede a su PROPIO comercio (A == A),
    /// el middleware SIEMPRE permite el paso al siguiente handler (no retorna 403).
    /// Esto verifica que el middleware no tiene falsos positivos.
    /// 
    /// **Validates: Requirements 3.7** (aspecto complementario: no bloquear accesos legítimos)
    /// </summary>
    [Property(MaxTest = 100)]
    public void SameTenant_Access_Always_Passes_Through(PositiveInt seedComercio)
    {
        var comercioId = seedComercio.Get;
        var segmentIndex = Math.Abs(comercioId) % TenantRouteSegments.Length;
        var routeSegment = TenantRouteSegments[segmentIndex];

        var (middleware, _, nextCalled) = CreateMiddleware();

        // Usuario autenticado como comercio A accede a su propio recurso (comercioId = A)
        var httpContext = CreateCrossTenantHttpContext(
            userComercioId: comercioId,
            targetComercioId: comercioId,
            routeSegment: routeSegment);

        middleware.InvokeAsync(httpContext).GetAwaiter().GetResult();

        // DEBE pasar al siguiente middleware
        Assert.True(nextCalled[0],
            $"El middleware debe permitir acceso cuando el usuario del comercio {comercioId} accede a su propio comercio");
        // NO debe retornar 403
        Assert.NotEqual(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
    }
}
