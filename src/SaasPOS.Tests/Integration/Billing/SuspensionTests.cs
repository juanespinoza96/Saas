using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Integration.Billing;

/// <summary>
/// Integration tests verifying that 3 days after mora:
/// - Subscription state becomes "Suspendido"
/// - Comercio.Estado becomes "Suspendido"
/// - Users of that comercio get 403 on API requests
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Billing")]
public class SuspensionTests : IntegrationTestBase
{
    public SuspensionTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_3DaysAfterMora_SetsSubscripcionSuspendido()
    {
        // Arrange: subscription "En mora" with corte date 3+ days ago
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context);

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3), // Corte was June 3
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "En mora",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process on June 6 (3 days after corte)
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 6, 6));

        // Assert: Subscription suspended
        await using var verifyContext = CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("Suspendido", updatedSub.Estado);
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_3DaysAfterMora_SetsComercioEstadoSuspendido()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0991100000001");

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3),
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "En mora",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process 3 days after corte
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 6, 6));

        // Assert: Comercio Estado = "Suspendido"
        await using var verifyContext = CreateDbContext();
        var updatedComercio = await verifyContext.Comercios
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == comercio.Id);
        Assert.Equal("Suspendido", updatedComercio.Estado);
        Assert.False(updatedComercio.Activo);
    }

    [DockerAvailableFact]
    public async Task SuspendedComercio_UserRequest_Returns403()
    {
        // Arrange: Create a suspended comercio with a user
        await using var context = CreateDbContext();
        var plan = new Plan
        {
            Nombre = "Plan Suspension",
            Precio = 350m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = "0992200000001",
            RazonSocial = "Comercio Suspendido API Test",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Estado = "Suspendido",
            FechaRegistro = DateTime.UtcNow
        };
        context.Comercios.Add(comercio);
        await context.SaveChangesAsync();

        var config = new ConfiguracionComercio
        {
            ComercioId = comercio.Id,
            EsBarEscolar = false,
            MostrarBotonCliente = false,
            PermiteVentaEnNegativo = false,
            ImpresionAutomaticaTicket = false
        };
        context.ConfiguracionesComercio.Add(config);

        var sucursal = new Sucursal
        {
            ComercioId = comercio.Id,
            Nombre = "Sucursal Suspended",
            Direccion = "Calle Test",
            Telefono = "0991234567",
            SerieFacturacion = "001-001"
        };
        context.Sucursales.Add(sucursal);
        await context.SaveChangesAsync();

        var usuario = new Usuario
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            Nombre = "Cajero Suspendido",
            Email = $"cajero-susp-{Guid.NewGuid():N}@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            Rol = "Cajero",
            Activo = true
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        // Authenticate as the user from the suspended comercio
        AuthenticateAs(usuario.Id, comercio.Id, "Cajero");

        // Act: Try to access a tenant-scoped endpoint
        var response = await Client.GetAsync("/api/tenants/productos");

        // Assert: Should be blocked (403 Forbidden)
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_2DaysAfterMora_DoesNotSuspend()
    {
        // Arrange: subscription "En mora" with corte date only 2 days ago (not yet 3)
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0993300000001");

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3), // Corte was June 3
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "En mora",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process on June 5 (only 2 days after corte, NOT 3)
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 6, 5));

        // Assert: Still "En mora", NOT suspended
        await using var verifyContext = CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("En mora", updatedSub.Estado);

        var updatedComercio = await verifyContext.Comercios
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == comercio.Id);
        Assert.Equal("Activo", updatedComercio.Estado);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private BillingService CreateBillingService(AppDbContext context)
    {
        var auditService = new AuditService(context);
        var emailService = new EmailService(context);
        var notificationService = new NotificationService(context, emailService);
        var jtiBlocklist = Mock.Of<IJtiBlocklist>();

        return new BillingService(
            context,
            auditService,
            notificationService,
            emailService,
            jtiBlocklist,
            Mock.Of<ILogger<BillingService>>());
    }

    private static async Task<(Plan plan, Comercio comercio)> SeedComercioWithPlan(
        AppDbContext context, string ruc = "0990000000001")
    {
        var plan = new Plan
        {
            Nombre = "Plan Suspension Test",
            Precio = 350m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = ruc,
            RazonSocial = $"Comercio Suspension {ruc}",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Activo = true,
            FechaRegistro = DateTime.UtcNow
        };
        context.Comercios.Add(comercio);
        await context.SaveChangesAsync();

        return (plan, comercio);
    }
}
