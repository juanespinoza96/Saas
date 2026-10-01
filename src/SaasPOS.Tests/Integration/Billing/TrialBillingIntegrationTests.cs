using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Integration.Billing;

/// <summary>
/// Tests de integración para el procesamiento de trials en BillingService.
/// Verifica notificaciones a 5, 2, 0 días, suspensión automática,
/// aislamiento de trials del ciclo regular, idempotencia y resiliencia.
/// Validates: Requirements 9.1, 9.2, 9.3, 9.4
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Billing")]
public class TrialBillingIntegrationTests : IntegrationTestBase
{
    public TrialBillingIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    // ══════════════════════════════════════════════════════════════════
    // Test 1: Notificación a 5 días de expirar
    // ══════════════════════════════════════════════════════════════════

    [DockerAvailableFact]
    public async Task ProcesarCortes_TrialA5Dias_CreaNotificacionYEmail()
    {
        // Arrange: Suscripción Trial con FechaProximoCorte = hoy + 5
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio = await SeedComercio(context, plan.Id);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var fechaCorte = hoy.AddDays(5);
        var suscripcion = await SeedTrialSuscripcion(context, comercio.Id, plan.Id, fechaCorte);
        var dueno = await SeedDueno(context, comercio.Id);

        var billingService = CreateBillingService(context);

        // Act
        await billingService.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue));

        // Assert: Se creó notificación "Trial por Expirar"
        await using var verifyContext = Fixture.CreateDbContext();
        var notificacion = await verifyContext.Notificaciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.SuscripcionId == suscripcion.Id
                && n.TipoNotificacion == "Trial por Expirar");

        Assert.NotNull(notificacion);
        Assert.Equal(comercio.Id, notificacion.ComercioId);
        Assert.Contains(comercio.RazonSocial, notificacion.Mensaje);

        // Assert: Email encolado al Dueño
        var email = await verifyContext.ColaCorreos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.ComercioId == comercio.Id
                && e.Destinatario == dueno.Email);

        Assert.NotNull(email);
        Assert.Contains("Trial por Expirar", email.Asunto);
    }

    // ══════════════════════════════════════════════════════════════════
    // Test 2: Notificación a 2 días de expirar
    // ══════════════════════════════════════════════════════════════════

    [DockerAvailableFact]
    public async Task ProcesarCortes_TrialA2Dias_CreaNotificacionTrialUrgente()
    {
        // Arrange: Suscripción Trial con FechaProximoCorte = hoy + 2
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio = await SeedComercio(context, plan.Id);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var fechaCorte = hoy.AddDays(2);
        var suscripcion = await SeedTrialSuscripcion(context, comercio.Id, plan.Id, fechaCorte);
        var dueno = await SeedDueno(context, comercio.Id);

        var billingService = CreateBillingService(context);

        // Act
        await billingService.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue));

        // Assert: Se creó notificación "Trial Urgente"
        await using var verifyContext = Fixture.CreateDbContext();
        var notificacion = await verifyContext.Notificaciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.SuscripcionId == suscripcion.Id
                && n.TipoNotificacion == "Trial Urgente");

        Assert.NotNull(notificacion);
        Assert.Equal(comercio.Id, notificacion.ComercioId);
        Assert.Contains(comercio.RazonSocial, notificacion.Mensaje);

        // Assert: Email encolado al Dueño
        var email = await verifyContext.ColaCorreos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.ComercioId == comercio.Id
                && e.Destinatario == dueno.Email);

        Assert.NotNull(email);
        Assert.Contains("Trial Urgente", email.Asunto);
    }

    // ══════════════════════════════════════════════════════════════════
    // Test 3: Notificación a 0 días + suspensión automática
    // ══════════════════════════════════════════════════════════════════

    [DockerAvailableFact]
    public async Task ProcesarCortes_TrialExpirado_CreaNotificacionYSuspendeComercio()
    {
        // Arrange: Suscripción Trial con FechaProximoCorte = hoy (0 días)
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio = await SeedComercio(context, plan.Id);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var suscripcion = await SeedTrialSuscripcion(context, comercio.Id, plan.Id, hoy);
        var dueno = await SeedDueno(context, comercio.Id);

        var billingService = CreateBillingService(context);

        // Act
        await billingService.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue));

        // Assert: Se creó notificación "Trial Expirado"
        await using var verifyContext = Fixture.CreateDbContext();
        var notificacion = await verifyContext.Notificaciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.SuscripcionId == suscripcion.Id
                && n.TipoNotificacion == "Trial Expirado");

        Assert.NotNull(notificacion);
        Assert.Contains(comercio.RazonSocial, notificacion.Mensaje);

        // Assert: Suscripción cambió a "Trial_Expirado"
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);
        Assert.Equal("Trial_Expirado", updatedSub.Estado);

        // Assert: Comercio cambió a "Suspendido"
        var updatedComercio = await verifyContext.Comercios
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == comercio.Id);
        Assert.Equal("Suspendido", updatedComercio.Estado);
    }

    // ══════════════════════════════════════════════════════════════════
    // Test 4: Idempotencia — no duplica notificaciones
    // ══════════════════════════════════════════════════════════════════

    [DockerAvailableFact]
    public async Task ProcesarCortes_EjecutadoDoble_NoDuplicaNotificaciones()
    {
        // Arrange: Suscripción Trial con FechaProximoCorte = hoy + 5
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio = await SeedComercio(context, plan.Id);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var fechaCorte = hoy.AddDays(5);
        var suscripcion = await SeedTrialSuscripcion(context, comercio.Id, plan.Id, fechaCorte);
        await SeedDueno(context, comercio.Id);

        var billingService = CreateBillingService(context);

        // Act: Ejecutar dos veces con la misma fecha
        await billingService.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue));
        await billingService.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue));

        // Assert: Solo 1 notificación, no duplicada
        await using var verifyContext = Fixture.CreateDbContext();
        var notificaciones = await verifyContext.Notificaciones
            .IgnoreQueryFilters()
            .Where(n => n.SuscripcionId == suscripcion.Id
                && n.TipoNotificacion == "Trial por Expirar")
            .ToListAsync();

        Assert.Single(notificaciones);
    }

    // ══════════════════════════════════════════════════════════════════
    // Test 5: Aislamiento — Trial no entra en lógica "Por vencer"
    // ══════════════════════════════════════════════════════════════════

    [DockerAvailableFact]
    public async Task ProcesarCortes_TrialConCorteA7Dias_NoTransicionaAPorVencer()
    {
        // Arrange: Suscripción Trial con FechaProximoCorte = hoy + 7
        // (normalmente dispararía "Por vencer" en suscripciones regulares)
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio = await SeedComercio(context, plan.Id);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var fechaCorte = hoy.AddDays(7);
        var suscripcion = await SeedTrialSuscripcion(context, comercio.Id, plan.Id, fechaCorte);
        await SeedDueno(context, comercio.Id);

        var billingService = CreateBillingService(context);

        // Act
        await billingService.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue));

        // Assert: El estado sigue siendo "Trial", NO "Por vencer"
        await using var verifyContext = Fixture.CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);

        Assert.Equal("Trial", updatedSub.Estado);
    }

    // ══════════════════════════════════════════════════════════════════
    // Test 6: Aislamiento — Trial expirado no entra en "En mora"
    // ══════════════════════════════════════════════════════════════════

    [DockerAvailableFact]
    public async Task ProcesarCortes_TrialConCorteHoy_NoTransicionaAEnMora()
    {
        // Arrange: Suscripción Trial con FechaProximoCorte = hoy
        // (normalmente dispararía "En mora" en suscripciones regulares)
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio = await SeedComercio(context, plan.Id);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var suscripcion = await SeedTrialSuscripcion(context, comercio.Id, plan.Id, hoy);
        await SeedDueno(context, comercio.Id);

        var billingService = CreateBillingService(context);

        // Act
        await billingService.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue));

        // Assert: El estado es "Trial_Expirado", NO "En mora"
        await using var verifyContext = Fixture.CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);

        Assert.Equal("Trial_Expirado", updatedSub.Estado);
    }

    // ══════════════════════════════════════════════════════════════════
    // Test 7: Trial convertido entra en ciclo regular de cobros
    // ══════════════════════════════════════════════════════════════════

    [DockerAvailableFact]
    public async Task ProcesarCortes_TrialConvertidoAActiva_EntraEnCicloRegular()
    {
        // Arrange: Suscripción ya convertida (Estado="Activa") con FechaProximoCorte = hoy + 7
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context, "Empresarial", 1200m);
        var comercio = await SeedComercio(context, plan.Id);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var fechaCorte = hoy.AddDays(7);

        // Simular suscripción convertida: estado "Activa" con precio real
        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = hoy.AddDays(-23), // Convertida hace 23 días
            FechaProximoCorte = fechaCorte,
            MontoCuota = 1200m,
            EsProporcional = false,
            Estado = "Activa",
            DiasExtendidos = 0
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();

        // Seed gerente (requerido por lógica regular de "Por vencer")
        var gerente = new Usuario
        {
            ComercioId = comercio.Id,
            Nombre = "Gerente Test",
            Email = $"gerente-{Guid.NewGuid():N}@test.com",
            PasswordHash = "$2a$12$dummyhash",
            Rol = "Gerente",
            Activo = true
        };
        context.Usuarios.Add(gerente);
        await context.SaveChangesAsync();

        var billingService = CreateBillingService(context);

        // Act
        await billingService.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue));

        // Assert: Estado cambió a "Por vencer" (ciclo regular la procesó)
        await using var verifyContext = Fixture.CreateDbContext();
        var updatedSub = await verifyContext.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == suscripcion.Id);

        Assert.Equal("Por vencer", updatedSub.Estado);
    }

    // ══════════════════════════════════════════════════════════════════
    // Test 8: Resiliencia — error en un trial no interrumpe los demás
    // ══════════════════════════════════════════════════════════════════

    [DockerAvailableFact]
    public async Task ProcesarCortes_ErrorEnUnTrial_OtrosTrialsSeProcesanCorrectamente()
    {
        // Arrange: Dos trials — uno válido y otro con datos corruptos (sin Comercio)
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio1 = await SeedComercio(context, plan.Id, "1111111111001");
        var comercio2 = await SeedComercio(context, plan.Id, "2222222222001");

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var fechaCorte = hoy.AddDays(5);

        // Trial 1: válido con dueño
        var trial1 = await SeedTrialSuscripcion(context, comercio1.Id, plan.Id, fechaCorte);
        await SeedDueno(context, comercio1.Id, "dueno1@test.com");

        // Trial 2: válido con dueño
        var trial2 = await SeedTrialSuscripcion(context, comercio2.Id, plan.Id, fechaCorte);
        await SeedDueno(context, comercio2.Id, "dueno2@test.com");

        // Forzar error en trial1: corromper ComercioId para causar NullReferenceException
        // cuando BillingService intente acceder a suscripcion.Comercio.RazonSocial.
        // Deshabilitamos triggers (incluye FK checks) en la tabla Suscripciones.
        await context.Database.ExecuteSqlRawAsync(
            @"ALTER TABLE ""Suscripciones"" DISABLE TRIGGER ALL");
        await context.Database.ExecuteSqlRawAsync(
            @"UPDATE ""Suscripciones"" SET ""ComercioId"" = -999 WHERE ""Id"" = {0}", trial1.Id);
        await context.Database.ExecuteSqlRawAsync(
            @"ALTER TABLE ""Suscripciones"" ENABLE TRIGGER ALL");

        var billingService = CreateBillingService(context);

        // Act: Procesar — el trial1 fallará, pero trial2 debe procesarse
        await billingService.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue));

        // Assert: Trial 2 tiene notificación (fue procesado a pesar del error en trial1)
        await using var verifyContext = Fixture.CreateDbContext();
        var notificacion2 = await verifyContext.Notificaciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.SuscripcionId == trial2.Id
                && n.TipoNotificacion == "Trial por Expirar");

        Assert.NotNull(notificacion2);
    }

    // ══════════════════════════════════════════════════════════════════
    // Helpers de seeding
    // ══════════════════════════════════════════════════════════════════

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

    private static async Task<Plan> SeedPlan(AppDbContext context, string nombre = "Básico", decimal precio = 350m)
    {
        var plan = new Plan { Nombre = nombre, Precio = precio, LimiteUsuarios = 2, LimiteAtributos = 2 };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();
        return plan;
    }

    private static async Task<Comercio> SeedComercio(AppDbContext context, int planId, string ruc = "1234567890001")
    {
        var comercio = new Comercio
        {
            Ruc = ruc,
            RazonSocial = $"Comercio Test {ruc}",
            PlanId = planId,
            UsaFacturacionSRI = false,
            Estado = "Activo",
            Activo = true,
            FechaRegistro = DateTime.UtcNow
        };
        context.Comercios.Add(comercio);
        await context.SaveChangesAsync();
        return comercio;
    }

    private static async Task<Suscripcion> SeedTrialSuscripcion(
        AppDbContext context, int comercioId, int planId, DateOnly fechaProximoCorte)
    {
        var suscripcion = new Suscripcion
        {
            ComercioId = comercioId,
            PlanId = planId,
            FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow),
            FechaProximoCorte = fechaProximoCorte,
            MontoCuota = 0,
            EsProporcional = false,
            Estado = "Trial",
            DiasExtendidos = 0
        };
        context.Suscripciones.Add(suscripcion);
        await context.SaveChangesAsync();
        return suscripcion;
    }

    private static async Task<Usuario> SeedDueno(
        AppDbContext context, int comercioId, string email = "dueno@test.com")
    {
        var dueno = new Usuario
        {
            ComercioId = comercioId,
            Nombre = "Dueño Test",
            Email = email,
            PasswordHash = "$2a$12$dummyhash",
            Rol = "Dueño",
            Activo = true
        };
        context.Usuarios.Add(dueno);
        await context.SaveChangesAsync();
        return dueno;
    }
}
