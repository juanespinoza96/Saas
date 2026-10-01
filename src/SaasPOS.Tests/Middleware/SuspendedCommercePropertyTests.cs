// Feature: prueba-gratuita, Property 9: Bloqueo de acceso a comercio suspendido
using System.Security.Claims;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasPOS.Api.Middleware;
using SaasPOS.Api.Services;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Tests.Middleware;

/// <summary>
/// Property 9: Bloqueo de acceso a comercio suspendido
/// Validates: Requirements 4.2
/// For any request a una ruta bajo /api/tenants/ de un Comercio con Estado "Suspendido",
/// el sistema SHALL responder con HTTP 403.
/// </summary>
public class SuspendedCommercePropertyTests
{
    /// <summary>
    /// Genera segmentos de ruta válidos para simular rutas de tenant.
    /// </summary>
    private static Arbitrary<string> TenantRouteSegmentArbitrary()
    {
        var segments = new[]
        {
            "productos", "ventas", "categorias", "clientes", "stock",
            "sucursales", "usuarios", "reportes", "configuracion",
            "facturas", "inventario", "dashboard", "perfil"
        };

        return Gen.Elements(segments).ToArbitrary();
    }

    /// <summary>
    /// Genera estados distintos a "Suspendido" que el comercio puede tener.
    /// </summary>
    private static Arbitrary<string> NonSuspendedStateArbitrary()
    {
        var states = new[] { "Activo" };
        return Gen.Elements(states).ToArbitrary();
    }

    /// <summary>
    /// Crea un AppDbContext InMemory con un Comercio con el estado indicado.
    /// Usa un AdminTenantContext (IsSuperAdmin=true) para evitar los query filters.
    /// </summary>
    private static AppDbContext CreateDbContextWithComercio(string dbName, int comercioId, string estado)
    {
        var adminTenantContext = new AdminTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var dbContext = new AppDbContext(options, adminTenantContext);

        // Crear un plan básico necesario para la FK
        if (!dbContext.Planes.Any(p => p.Id == 1))
        {
            dbContext.Planes.Add(new Plan
            {
                Id = 1,
                Nombre = "Básico",
                Precio = 350m,
                LimiteUsuarios = 2,
                LimiteSucursales = 1,
                LimiteAtributos = 2
            });
        }

        // Crear o actualizar el Comercio con el estado dado
        var comercio = dbContext.Comercios.Find(comercioId);
        if (comercio == null)
        {
            dbContext.Comercios.Add(new Comercio
            {
                Id = comercioId,
                Ruc = "1234567890001",
                RazonSocial = "Test Comercio",
                PlanId = 1,
                Estado = estado,
                FechaRegistro = DateTime.UtcNow
            });
        }
        else
        {
            comercio.Estado = estado;
        }

        dbContext.SaveChanges();
        return dbContext;
    }

    /// <summary>
    /// Crea un HttpContext con los servicios necesarios para el middleware.
    /// </summary>
    private static HttpContext CreateHttpContext(string path, int comercioId, AppDbContext dbContext)
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

        // Configurar los servicios requeridos
        var services = new ServiceCollection();
        services.AddScoped<AppDbContext>(_ => dbContext);

        // HttpContextAccessor necesario para TenantContext
        var httpContextAccessor = new HttpContextAccessor { HttpContext = context };
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddScoped<TenantContext>();
        services.AddScoped<TenantContextAccessor>();

        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    /// <summary>
    /// Propiedad: Cualquier request a /api/tenants/{segmento} de un comercio con Estado "Suspendido"
    /// SIEMPRE retorna HTTP 403.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Comercio_Suspendido_Siempre_Retorna_403(int seed)
    {
        // Generar un segmento de ruta aleatorio
        var segments = new[]
        {
            "productos", "ventas", "categorias", "clientes", "stock",
            "sucursales", "usuarios", "reportes", "configuracion",
            "facturas", "inventario", "dashboard", "perfil"
        };
        var segment = segments[Math.Abs(seed) % segments.Length];
        var path = $"/api/tenants/{segment}";

        var comercioId = 100 + Math.Abs(seed % 1000);
        var dbName = $"SuspendedTest_403_{seed}_{Guid.NewGuid()}";

        using var dbContext = CreateDbContextWithComercio(dbName, comercioId, "Suspendido");

        var nextCalled = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var httpContext = CreateHttpContext(path, comercioId, dbContext);

        middleware.InvokeAsync(httpContext).GetAwaiter().GetResult();

        // El middleware DEBE retornar 403 y NO llamar al siguiente middleware
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        Assert.False(nextCalled, $"El middleware no debería pasar al siguiente handler cuando el comercio está Suspendido (ruta: {path})");
    }

    /// <summary>
    /// Propiedad: Cualquier request a /api/tenants/{segmento} de un comercio con Estado != "Suspendido"
    /// SIEMPRE pasa al siguiente middleware (no retorna 403).
    /// </summary>
    [Property(MaxTest = 100)]
    public void Comercio_No_Suspendido_Siempre_Pasa_Al_Siguiente_Middleware(int seed)
    {
        var segments = new[]
        {
            "productos", "ventas", "categorias", "clientes", "stock",
            "sucursales", "usuarios", "reportes", "configuracion",
            "facturas", "inventario", "dashboard", "perfil"
        };
        var segment = segments[Math.Abs(seed) % segments.Length];
        var path = $"/api/tenants/{segment}";

        var comercioId = 2000 + Math.Abs(seed % 1000);
        var dbName = $"SuspendedTest_Pass_{seed}_{Guid.NewGuid()}";

        // Estado "Activo" - no suspendido
        using var dbContext = CreateDbContextWithComercio(dbName, comercioId, "Activo");

        var nextCalled = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var httpContext = CreateHttpContext(path, comercioId, dbContext);

        middleware.InvokeAsync(httpContext).GetAwaiter().GetResult();

        // El middleware DEBE pasar al siguiente handler y NO retornar 403
        Assert.True(nextCalled, $"El middleware debería pasar al siguiente handler cuando el comercio está Activo (ruta: {path})");
        Assert.NotEqual(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
    }
}
