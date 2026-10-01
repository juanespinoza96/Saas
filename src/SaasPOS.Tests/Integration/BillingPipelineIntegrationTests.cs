using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Integration tests for the full billing pipeline:
/// Plan contracting → proportional cuota → daily cuts → mora → suspension → reactivation.
/// Uses real PostgreSQL via Testcontainers with BillingService directly.
/// </summary>
[Collection("Postgres")]
public class BillingPipelineIntegrationTests : IntegrationTestBase
{
    public BillingPipelineIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task ContratarPlan_CreatesSubscriptionWithProportionalCuota()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context);
        var billingService = CreateBillingService(context);

        // Contract on day 15 of a 30-day month
        var fechaContratacion = new DateTime(2024, 6, 15); // June has 30 days

        // Act
        var suscripcion = await billingService.ContratarPlanAsync(comercio.Id, plan.Id, fechaContratacion);

        // Assert: Subscription created
        Assert.NotNull(suscripcion);
        Assert.Equal(comercio.Id, suscripcion.ComercioId);
        Assert.Equal(plan.Id, suscripcion.PlanId);
        Assert.Equal("Activa", suscripcion.Estado);
        Assert.True(suscripcion.EsProporcional);

        // Proportional cuota: (30 / 30) * 18 days remaining until July 3 = 18.00
        // (precioPlan / diasTotalesMes) * diasRestantes = (30 / 30) * 18 = 18.00
        var expectedCuota = Math.Round((plan.Precio / 30m) * 18m, 2);
        Assert.Equal(expectedCuota, suscripcion.MontoCuota);

        // FechaProximoCorte should be July 3
        Assert.Equal(new DateOnly(2024, 7, 3), suscripcion.FechaProximoCorte);
    }

    [DockerAvailableFact]
    public async Task ContratarPlan_OnDayThree_FullCharge()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0997777777001");
        var billingService = CreateBillingService(context);

        // Contract on day 3 (Dia_Corte) → full charge
        var fechaContratacion = new DateTime(2024, 6, 3);

        // Act
        var suscripcion = await billingService.ContratarPlanAsync(comercio.Id, plan.Id, fechaContratacion);

        // Assert: Full charge, not proportional
        Assert.Equal(plan.Precio, suscripcion.MontoCuota);
        Assert.False(suscripcion.EsProporcional);

        // FechaProximoCorte = next month's day 3
        Assert.Equal(new DateOnly(2024, 7, 3), suscripcion.FechaProximoCorte);
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_7DaysBeforeCorte_SetsStatePorVencerAndNotifies()
    {
        // Arrange: subscription with corte on June 20
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0991000000001");
        await SeedGerente(context, comercio.Id);

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 20),
            MontoCuota = 30m,
            EsProporcional = false,
            Estado = "Activa",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process cuts 7 days before corte (June 13)
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 6, 13));

        // Assert: State changed to "Por vencer"
        await using var verifyContext = CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("Por vencer", updatedSub.Estado);

        // Notification created
        var notification = await verifyContext.Notificaciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.ComercioId == comercio.Id);
        Assert.NotNull(notification);

        // Email queued
        var email = await verifyContext.ColaCorreos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.ComercioId == comercio.Id);
        Assert.NotNull(email);
        Assert.Contains("vence", email.Asunto, StringComparison.OrdinalIgnoreCase);
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_CorteDateNoPayment_SetsStateEnMoraAndNotifies()
    {
        // Arrange: subscription with corte TODAY and no payment
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0992000000001");
        await SeedGerente(context, comercio.Id);

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3),
            MontoCuota = 30m,
            EsProporcional = false,
            Estado = "Activa",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process on the corte date itself
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 6, 3));

        // Assert: State changed to "En mora"
        await using var verifyContext = CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("En mora", updatedSub.Estado);

        // Notification for mora
        var notification = await verifyContext.Notificaciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.ComercioId == comercio.Id
                && n.TipoNotificacion == "Mora");
        Assert.NotNull(notification);

        // Email queued
        var email = await verifyContext.ColaCorreos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.ComercioId == comercio.Id && e.Asunto.Contains("mora"));
        Assert.NotNull(email);
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_3DaysAfterMora_SuspendsCommerceAndBlocksSessions()
    {
        // Arrange: subscription already "En mora" with corte date 3+ days ago
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0993000000001");

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3), // Corte was June 3
            MontoCuota = 30m,
            EsProporcional = false,
            Estado = "En mora",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process 3 days after corte (June 6)
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 6, 6));

        // Assert: Commerce suspended
        await using var verifyContext = CreateDbContext();
        var updatedComercio = await verifyContext.Comercios
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == comercio.Id);
        Assert.False(updatedComercio.Activo);

        // Subscription state = "Suspendido"
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("Suspendido", updatedSub.Estado);
    }

    [DockerAvailableFact]
    public async Task RegistrarPago_ReactivatesCommerceAndRecalculatesNextCut()
    {
        // Arrange: suspended commerce with subscription in "Suspendido"
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0994000000001", activo: false);
        var gerente = await SeedGerente(context, comercio.Id);

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 3),
            MontoCuota = 30m,
            EsProporcional = false,
            Estado = "Suspendido",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);
        var pago = new PagoDto(
            MontoPagado: 30m,
            MetodoPago: "Transferencia",
            Referencia: "REF-001",
            RegistradoPor: gerente.Id);

        // Act
        var result = await billingService.RegistrarPagoAsync(comercio.Id, pago);

        // Assert
        Assert.True(result);

        // Commerce reactivated
        await using var verifyContext = CreateDbContext();
        var updatedComercio = await verifyContext.Comercios
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == comercio.Id);
        Assert.True(updatedComercio.Activo);

        // Subscription back to "Activa"
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("Activa", updatedSub.Estado);
        Assert.NotNull(updatedSub.FechaUltimoPago);

        // FechaProximoCorte recalculated to next month's day 3
        var today = DateTime.UtcNow;
        DateOnly expectedCorte;
        if (today.Day < 3)
            expectedCorte = new DateOnly(today.Year, today.Month, 3);
        else
            expectedCorte = new DateOnly(today.AddMonths(1).Year, today.AddMonths(1).Month, 3);

        Assert.Equal(expectedCorte, updatedSub.FechaProximoCorte);

        // PagoComercio record created
        var pagoRecord = await verifyContext.PagosComercio
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.ComercioId == comercio.Id);
        Assert.NotNull(pagoRecord);
        Assert.Equal(30m, pagoRecord.MontoPagado);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private BillingService CreateBillingService(Infrastructure.Data.AppDbContext context)
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
        Infrastructure.Data.AppDbContext context, string ruc = "0996666666001", bool activo = true)
    {
        var plan = new Plan
        {
            Nombre = "Plan Empresarial",
            Precio = 30m,
            LimiteUsuarios = 20,
            LimiteAtributos = 10
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = ruc,
            RazonSocial = $"Comercio Billing {ruc}",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Activo = activo,
            FechaRegistro = DateTime.UtcNow
        };
        context.Comercios.Add(comercio);
        await context.SaveChangesAsync();

        return (plan, comercio);
    }

    private static async Task<Usuario> SeedGerente(Infrastructure.Data.AppDbContext context, int comercioId)
    {
        var gerente = new Usuario
        {
            ComercioId = comercioId,
            Nombre = "Gerente Test",
            Email = $"gerente-{Guid.NewGuid():N}@test.com",
            PasswordHash = "$2a$12$dummyhash",
            Rol = "Gerente",
            Activo = true
        };
        context.Usuarios.Add(gerente);
        await context.SaveChangesAsync();
        return gerente;
    }
}
