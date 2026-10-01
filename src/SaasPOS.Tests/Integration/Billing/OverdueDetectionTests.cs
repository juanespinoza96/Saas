using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Integration.Billing;

/// <summary>
/// Integration tests verifying that ProcesarCortesDiarios correctly detects
/// subscriptions 7 days before cut date and creates notification + email.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Billing")]
public class OverdueDetectionTests : IntegrationTestBase
{
    public OverdueDetectionTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_7DaysBeforeCorte_SetsEstadoPorVencer()
    {
        // Arrange: subscription with corte on July 10, today is July 3 (7 days before)
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context);
        await SeedGerente(context, comercio.Id);

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 6, 3),
            FechaProximoCorte = new DateOnly(2024, 7, 10),
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "Activa",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process on July 3 (exactly 7 days before corte July 10)
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 7, 3));

        // Assert: State changed to "Por vencer"
        await using var verifyContext = CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("Por vencer", updatedSub.Estado);
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_7DaysBeforeCorte_CreatesNotification()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0991100000001");
        await SeedGerente(context, comercio.Id);

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 6, 20),
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "Activa",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process 7 days before corte (June 13)
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 6, 13));

        // Assert: Notification created for the comercio
        await using var verifyContext = CreateDbContext();
        var notification = await verifyContext.Notificaciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.ComercioId == comercio.Id);
        Assert.NotNull(notification);
        Assert.Contains("pago", notification.Titulo, StringComparison.OrdinalIgnoreCase);
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_7DaysBeforeCorte_QueuesEmail()
    {
        // Arrange
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0992200000001");
        await SeedGerente(context, comercio.Id);

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 5, 3),
            FechaProximoCorte = new DateOnly(2024, 8, 10),
            MontoCuota = 750m,
            EsProporcional = false,
            Estado = "Activa",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process 7 days before corte (August 3)
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 8, 3));

        // Assert: Email queued with subject about vencimiento
        await using var verifyContext = CreateDbContext();
        var email = await verifyContext.ColaCorreos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.ComercioId == comercio.Id);
        Assert.NotNull(email);
        Assert.Contains("venc", email.Asunto, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Pendiente", email.Estado);
    }

    [DockerAvailableFact]
    public async Task ProcesarCortes_NotExactly7Days_DoesNotTriggerPorVencer()
    {
        // Arrange: subscription with corte on July 10, today is July 4 (6 days before, not 7)
        await using var context = CreateDbContext();
        var (plan, comercio) = await SeedComercioWithPlan(context, ruc: "0993300000001");
        await SeedGerente(context, comercio.Id);

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = new DateOnly(2024, 6, 3),
            FechaProximoCorte = new DateOnly(2024, 7, 10),
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "Activa",
            FechaUltimoPago = null
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act: Process on July 4 (only 6 days before, should NOT trigger)
        await billingService.ProcesarCortesDiariosAsync(new DateTime(2024, 7, 4));

        // Assert: State remains "Activa"
        await using var verifyContext = CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("Activa", updatedSub.Estado);
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
            Nombre = "Plan Overdue Test",
            Precio = 350m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = ruc,
            RazonSocial = $"Comercio Overdue {ruc}",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Activo = true,
            FechaRegistro = DateTime.UtcNow
        };
        context.Comercios.Add(comercio);
        await context.SaveChangesAsync();

        return (plan, comercio);
    }

    private static async Task<Usuario> SeedGerente(AppDbContext context, int comercioId)
    {
        var gerente = new Usuario
        {
            ComercioId = comercioId,
            Nombre = "Gerente Overdue Test",
            Email = $"gerente-overdue-{Guid.NewGuid():N}@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            Rol = "Gerente",
            Activo = true
        };
        context.Usuarios.Add(gerente);
        await context.SaveChangesAsync();
        return gerente;
    }
}
