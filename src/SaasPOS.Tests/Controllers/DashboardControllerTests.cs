using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SaasPOS.Api.Controllers.Admin;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Tests.Controllers;

public class DashboardControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly DashboardController _controller;

    public DashboardControllerTests()
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContext.Setup(t => t.ComercioId).Returns(0);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options, tenantContext.Object);
        _controller = new DashboardController(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task SeedPlanes()
    {
        _db.Planes.AddRange(
            new Plan { Id = 1, Nombre = "Básico", Precio = 29.99m, LimiteUsuarios = 3, LimiteAtributos = 5 },
            new Plan { Id = 2, Nombre = "Profesional", Precio = 59.99m, LimiteUsuarios = 10, LimiteAtributos = 10 }
        );
        await _db.SaveChangesAsync();
    }

    private async Task<Comercio> SeedComercio(int planId, bool activo = true, string ruc = "0991234567001")
    {
        var comercio = new Comercio
        {
            Ruc = ruc,
            RazonSocial = $"Comercio {ruc}",
            PlanId = planId,
            Activo = activo,
            FechaRegistro = DateTime.UtcNow
        };
        _db.Comercios.Add(comercio);
        await _db.SaveChangesAsync();
        return comercio;
    }

    [Fact]
    public async Task GetDashboard_EmptyDatabase_ReturnsEmptyDashboard()
    {
        var result = await _controller.GetDashboard();

        var ok = Assert.IsType<OkObjectResult>(result);
        var dashboard = Assert.IsType<DashboardDto>(ok.Value);
        Assert.Empty(dashboard.IngresosPorPlan);
        Assert.Equal(0m, dashboard.TotalMensualProyectado);
        Assert.Empty(dashboard.Comercios);
    }

    [Fact]
    public async Task GetDashboard_IngresosPorPlan_GroupsByPlanName()
    {
        await SeedPlanes();
        var comercio1 = await SeedComercio(1, ruc: "0991111111001");
        var comercio2 = await SeedComercio(2, ruc: "0992222222001");

        // Add suscripciones (needed for pagos FK)
        var suscripcion1 = new Suscripcion
        {
            ComercioId = comercio1.Id,
            PlanId = 1,
            FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow),
            FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            MontoCuota = 29.99m,
            Estado = "Activa"
        };
        var suscripcion2 = new Suscripcion
        {
            ComercioId = comercio2.Id,
            PlanId = 2,
            FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow),
            FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            MontoCuota = 59.99m,
            Estado = "Activa"
        };
        _db.Suscripciones.AddRange(suscripcion1, suscripcion2);
        await _db.SaveChangesAsync();

        // Seed a user for RegistradoPor
        var usuario = new Usuario
        {
            Nombre = "Admin",
            Email = "admin@test.com",
            PasswordHash = "hash",
            Rol = "SuperAdmin",
            ComercioId = comercio1.Id
        };
        _db.Usuarios.Add(usuario);
        await _db.SaveChangesAsync();

        // Add payments
        _db.PagosComercio.AddRange(
            new PagoComercio { ComercioId = comercio1.Id, SuscripcionId = suscripcion1.Id, MontoPagado = 29.99m, FechaPago = DateOnly.FromDateTime(DateTime.UtcNow), MetodoPago = "Transferencia", RegistradoPor = usuario.Id },
            new PagoComercio { ComercioId = comercio1.Id, SuscripcionId = suscripcion1.Id, MontoPagado = 29.99m, FechaPago = DateOnly.FromDateTime(DateTime.UtcNow), MetodoPago = "Transferencia", RegistradoPor = usuario.Id },
            new PagoComercio { ComercioId = comercio2.Id, SuscripcionId = suscripcion2.Id, MontoPagado = 59.99m, FechaPago = DateOnly.FromDateTime(DateTime.UtcNow), MetodoPago = "Transferencia", RegistradoPor = usuario.Id }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetDashboard();

        var ok = Assert.IsType<OkObjectResult>(result);
        var dashboard = Assert.IsType<DashboardDto>(ok.Value);

        Assert.Equal(2, dashboard.IngresosPorPlan.Count);
        var basico = dashboard.IngresosPorPlan.First(i => i.PlanNombre == "Básico");
        var profesional = dashboard.IngresosPorPlan.First(i => i.PlanNombre == "Profesional");
        Assert.Equal(59.98m, basico.TotalIngresos);
        Assert.Equal(59.99m, profesional.TotalIngresos);
    }

    [Fact]
    public async Task GetDashboard_TotalMensualProyectado_SumsActiveSubscriptions()
    {
        await SeedPlanes();
        var comercio1 = await SeedComercio(1, activo: true, ruc: "0991111111001");
        var comercio2 = await SeedComercio(2, activo: true, ruc: "0992222222001");
        var comercioInactivo = await SeedComercio(1, activo: false, ruc: "0993333333001");

        _db.Suscripciones.AddRange(
            new Suscripcion { ComercioId = comercio1.Id, PlanId = 1, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), MontoCuota = 29.99m, Estado = "Activa" },
            new Suscripcion { ComercioId = comercio2.Id, PlanId = 2, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), MontoCuota = 59.99m, Estado = "Activa" },
            // Inactive comercio — should NOT count
            new Suscripcion { ComercioId = comercioInactivo.Id, PlanId = 1, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), MontoCuota = 29.99m, Estado = "Activa" },
            // Expired subscription — should NOT count
            new Suscripcion { ComercioId = comercio1.Id, PlanId = 1, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)), MontoCuota = 29.99m, Estado = "En mora" }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetDashboard();

        var ok = Assert.IsType<OkObjectResult>(result);
        var dashboard = Assert.IsType<DashboardDto>(ok.Value);

        // Only active subscriptions of active comercios: 29.99 + 59.99
        Assert.Equal(89.98m, dashboard.TotalMensualProyectado);
    }

    [Fact]
    public async Task GetDashboard_ComercioEstados_DeterminesStateCorrectly()
    {
        await SeedPlanes();
        var comercioActivo = await SeedComercio(1, activo: true, ruc: "0991111111001");
        var comercioSuspendido = await SeedComercio(1, activo: false, ruc: "0992222222001");
        var comercioPorVencer = await SeedComercio(2, activo: true, ruc: "0993333333001");
        var comercioEnMora = await SeedComercio(2, activo: true, ruc: "0994444444001");

        _db.Suscripciones.AddRange(
            new Suscripcion { ComercioId = comercioActivo.Id, PlanId = 1, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), MontoCuota = 29.99m, Estado = "Activa" },
            new Suscripcion { ComercioId = comercioSuspendido.Id, PlanId = 1, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), MontoCuota = 29.99m, Estado = "Activa" },
            new Suscripcion { ComercioId = comercioPorVencer.Id, PlanId = 2, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), MontoCuota = 59.99m, Estado = "Por vencer" },
            new Suscripcion { ComercioId = comercioEnMora.Id, PlanId = 2, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), MontoCuota = 59.99m, Estado = "En mora" }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetDashboard();

        var ok = Assert.IsType<OkObjectResult>(result);
        var dashboard = Assert.IsType<DashboardDto>(ok.Value);

        Assert.Equal(4, dashboard.Comercios.Count);

        var activo = dashboard.Comercios.First(c => c.Id == comercioActivo.Id);
        Assert.Equal("Activo", activo.Estado);

        var suspendido = dashboard.Comercios.First(c => c.Id == comercioSuspendido.Id);
        Assert.Equal("Suspendido", suspendido.Estado);

        var porVencer = dashboard.Comercios.First(c => c.Id == comercioPorVencer.Id);
        Assert.Equal("Por vencer", porVencer.Estado);

        var enMora = dashboard.Comercios.First(c => c.Id == comercioEnMora.Id);
        Assert.Equal("En mora", enMora.Estado);
    }

    [Fact]
    public async Task GetDashboard_ComercioSinSuscripcion_ShowsActivo()
    {
        await SeedPlanes();
        var comercio = await SeedComercio(1, activo: true, ruc: "0991111111001");

        var result = await _controller.GetDashboard();

        var ok = Assert.IsType<OkObjectResult>(result);
        var dashboard = Assert.IsType<DashboardDto>(ok.Value);

        var dto = Assert.Single(dashboard.Comercios);
        Assert.Equal("Activo", dto.Estado);
        Assert.Null(dto.FechaProximoCorte);
    }

    [Fact]
    public async Task GetDashboard_ComercioWithMultipleSuscripciones_UsesLatest()
    {
        await SeedPlanes();
        var comercio = await SeedComercio(1, activo: true, ruc: "0991111111001");

        // Older subscription (Activa) and newer subscription (En mora)
        _db.Suscripciones.AddRange(
            new Suscripcion { ComercioId = comercio.Id, PlanId = 1, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), MontoCuota = 29.99m, Estado = "Activa" },
            new Suscripcion { ComercioId = comercio.Id, PlanId = 1, FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow), FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), MontoCuota = 29.99m, Estado = "En mora" }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetDashboard();

        var ok = Assert.IsType<OkObjectResult>(result);
        var dashboard = Assert.IsType<DashboardDto>(ok.Value);

        var dto = Assert.Single(dashboard.Comercios);
        Assert.Equal("En mora", dto.Estado);
    }
}
