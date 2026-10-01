using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Integration.Billing;

/// <summary>
/// Integration tests verifying proportional quota calculation:
/// - Day 3 (Dia_Corte) → full plan price
/// - Any other day → proportional based on remaining days until next day 3
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Billing")]
public class ProportionalQuotaTests : IntegrationTestBase
{
    public ProportionalQuotaTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task ContratarPlan_OnDay3_ChargesFullPlanPrice()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, precio: 350m);
        var billingService = CreateBillingService(context);

        // Day 3 = Dia_Corte → should charge full price
        var fechaContratacion = new DateTime(2024, 3, 3);

        // Act
        var suscripcion = await billingService.ContratarPlanAsync(comercio.Id, plan.Id, fechaContratacion);

        // Assert: Full price charged, not proportional
        Assert.Equal(350m, suscripcion.MontoCuota);
        Assert.False(suscripcion.EsProporcional);
        Assert.Equal("Activa", suscripcion.Estado);
        // Next cut date is next month's day 3
        Assert.Equal(new DateOnly(2024, 4, 3), suscripcion.FechaProximoCorte);
    }

    [DockerAvailableFact]
    public async Task ContratarPlan_AfterDay3_ChargesProportionalAmount()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, precio: 350m, ruc: "0991111111001");
        var billingService = CreateBillingService(context);

        // Day 15 of January (31-day month) → next day 3 is Feb 3 → 19 days remaining
        var fechaContratacion = new DateTime(2024, 1, 15);

        // Act
        var suscripcion = await billingService.ContratarPlanAsync(comercio.Id, plan.Id, fechaContratacion);

        // Assert: Proportional = (350 / 31) * 19 = 214.52 (rounded to 2 decimals)
        var expectedCuota = Math.Round((350m / 31m) * 19m, 2);
        Assert.Equal(expectedCuota, suscripcion.MontoCuota);
        Assert.True(suscripcion.EsProporcional);
        Assert.Equal(new DateOnly(2024, 2, 3), suscripcion.FechaProximoCorte);
    }

    [DockerAvailableFact]
    public async Task ContratarPlan_BeforeDay3_ChargesProportionalForCurrentMonth()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, precio: 750m, ruc: "0992222222001");
        var billingService = CreateBillingService(context);

        // Day 1 of March (31-day month) → next day 3 is March 3 → 2 days remaining
        var fechaContratacion = new DateTime(2024, 3, 1);

        // Act
        var suscripcion = await billingService.ContratarPlanAsync(comercio.Id, plan.Id, fechaContratacion);

        // Assert: Proportional = (750 / 31) * 2 = 48.39
        var expectedCuota = Math.Round((750m / 31m) * 2m, 2);
        Assert.Equal(expectedCuota, suscripcion.MontoCuota);
        Assert.True(suscripcion.EsProporcional);
        // Next cut is March 3 (same month since day < 3)
        Assert.Equal(new DateOnly(2024, 3, 3), suscripcion.FechaProximoCorte);
    }

    [DockerAvailableFact]
    public async Task ContratarPlan_LastDayOfMonth_ChargesProportionalUntilNextMonth3()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, precio: 1200m, ruc: "0993333333001");
        var billingService = CreateBillingService(context);

        // Day 31 of January → next day 3 is Feb 3 → 3 days remaining
        var fechaContratacion = new DateTime(2024, 1, 31);

        // Act
        var suscripcion = await billingService.ContratarPlanAsync(comercio.Id, plan.Id, fechaContratacion);

        // Assert: Proportional = (1200 / 31) * 3 = 116.13
        var expectedCuota = Math.Round((1200m / 31m) * 3m, 2);
        Assert.Equal(expectedCuota, suscripcion.MontoCuota);
        Assert.True(suscripcion.EsProporcional);
        Assert.Equal(new DateOnly(2024, 2, 3), suscripcion.FechaProximoCorte);
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
        AppDbContext context, decimal precio = 350m, string ruc = "0990000000001")
    {
        var plan = new Plan
        {
            Nombre = "Plan Test Proporcional",
            Precio = precio,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = ruc,
            RazonSocial = $"Comercio Proporcional {ruc}",
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
