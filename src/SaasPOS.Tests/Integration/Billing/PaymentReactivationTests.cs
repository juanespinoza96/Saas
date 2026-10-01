using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Integration.Billing;

/// <summary>
/// Integration tests verifying that payment of a suspended comercio
/// reactivates it with a new cut date (FechaProximoCorte).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Billing")]
public class PaymentReactivationTests : IntegrationTestBase
{
    public PaymentReactivationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task RegistrarPago_SuspendedComercio_ReactivatesComercioEstado()
    {
        // Arrange: Suspended comercio with subscription in "Suspendido"
        await using var context = CreateDbContext();
        var (plan, comercio, gerente) = await SeedSuspendedComercio(context);

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3),
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "Suspendido",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);
        var pago = new PagoDto(
            MontoPagado: 350m,
            MetodoPago: "Transferencia",
            Referencia: "REF-REACTIVACION-001",
            RegistradoPor: gerente.Id);

        // Act
        var result = await billingService.RegistrarPagoAsync(comercio.Id, pago);

        // Assert: Payment succeeded
        Assert.True(result);

        // Comercio reactivated to "Activo"
        await using var verifyContext = CreateDbContext();
        var updatedComercio = await verifyContext.Comercios
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == comercio.Id);
        Assert.Equal("Activo", updatedComercio.Estado);
        Assert.True(updatedComercio.Activo);
    }

    [DockerAvailableFact]
    public async Task RegistrarPago_SuspendedComercio_SetsSubscripcionActiva()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio, gerente) = await SeedSuspendedComercio(context, ruc: "0991100000001");

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3),
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "Suspendido",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);
        var pago = new PagoDto(
            MontoPagado: 350m,
            MetodoPago: "Efectivo",
            Referencia: null,
            RegistradoPor: gerente.Id);

        // Act
        await billingService.RegistrarPagoAsync(comercio.Id, pago);

        // Assert: Subscription state back to "Activa"
        await using var verifyContext = CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("Activa", updatedSub.Estado);
        Assert.NotNull(updatedSub.FechaUltimoPago);
    }

    [DockerAvailableFact]
    public async Task RegistrarPago_SuspendedComercio_RecalculatesFechaProximoCorte()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio, gerente) = await SeedSuspendedComercio(context, ruc: "0992200000001");

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3), // Old corte
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "Suspendido",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);
        var pago = new PagoDto(
            MontoPagado: 350m,
            MetodoPago: "Tarjeta",
            Referencia: "TXN-12345",
            RegistradoPor: gerente.Id);

        // Act
        await billingService.RegistrarPagoAsync(comercio.Id, pago);

        // Assert: FechaProximoCorte recalculated to next month's day 3
        await using var verifyContext = CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);

        var today = DateTime.UtcNow;
        DateOnly expectedCorte;
        if (today.Day < 3)
            expectedCorte = new DateOnly(today.Year, today.Month, 3);
        else
            expectedCorte = new DateOnly(today.AddMonths(1).Year, today.AddMonths(1).Month, 3);

        Assert.Equal(expectedCorte, updatedSub.FechaProximoCorte);
    }

    [DockerAvailableFact]
    public async Task RegistrarPago_SuspendedComercio_CreatesPagoComercioRecord()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio, gerente) = await SeedSuspendedComercio(context, ruc: "0993300000001");

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3),
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "Suspendido",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);
        var pago = new PagoDto(
            MontoPagado: 350m,
            MetodoPago: "Transferencia",
            Referencia: "REF-PAGO-RECORD",
            RegistradoPor: gerente.Id);

        // Act
        await billingService.RegistrarPagoAsync(comercio.Id, pago);

        // Assert: PagoComercio record exists
        await using var verifyContext = CreateDbContext();
        var pagoRecord = await verifyContext.PagosComercio
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.ComercioId == comercio.Id);
        Assert.NotNull(pagoRecord);
        Assert.Equal(350m, pagoRecord.MontoPagado);
        Assert.Equal("Transferencia", pagoRecord.MetodoPago);
        Assert.Equal("REF-PAGO-RECORD", pagoRecord.Referencia);
        Assert.Equal(gerente.Id, pagoRecord.RegistradoPor);
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

    private static async Task<(Plan plan, Comercio comercio, Usuario gerente)> SeedSuspendedComercio(
        AppDbContext context, string ruc = "0990000000001")
    {
        var plan = new Plan
        {
            Nombre = "Plan Reactivation Test",
            Precio = 350m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = ruc,
            RazonSocial = $"Comercio Suspended {ruc}",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Estado = "Suspendido", // Already suspended
            FechaRegistro = DateTime.UtcNow
        };
        context.Comercios.Add(comercio);
        await context.SaveChangesAsync();

        var gerente = new Usuario
        {
            ComercioId = comercio.Id,
            Nombre = "Gerente Reactivation",
            Email = $"gerente-react-{Guid.NewGuid():N}@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            Rol = "Gerente",
            Activo = true
        };
        context.Usuarios.Add(gerente);
        await context.SaveChangesAsync();

        return (plan, comercio, gerente);
    }
}
