using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Tests de integración para el flujo completo de trial:
/// Creación → extensión → expiración → suspensión → conversión → reactivación.
/// Verifica atomicidad, bloqueo de middleware y performance.
/// Validates: Requirements 1.8, 4.2, 8.2
/// </summary>
[Collection("Postgres")]
public class TrialFlowIntegrationTests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TrialFlowIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
        // Seed plan y usuario SuperAdmin requerido por JtiValidationMiddleware
        // para tests que autentican como SuperAdmin (userId: 1)
        await SeedBaseDataAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 1: Flujo de vida completo del trial
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifica el ciclo de vida completo:
    /// Crear trial → verificar estado → extender → verificar nueva fecha →
    /// simular expiración → verificar suspensión → convertir → verificar reactivación.
    /// </summary>
    [DockerAvailableFact]
    public async Task TrialLifecycle_CreateExtendExpireConvert_FullFlow()
    {
        // ── Arrange: seed Plan Básico ──
        await using var seedCtx = CreateDbContext();
        var planBasico = new Plan
        {
            Nombre = "Básico",
            Precio = 350m,
            LimiteUsuarios = 5,
            LimiteAtributos = 3
        };
        seedCtx.Planes.Add(planBasico);
        await seedCtx.SaveChangesAsync();

        // Seed SuperAdmin user (requerido por FK en LogsAuditoria.UsuarioId)
        await SeedSuperAdminUser(seedCtx, planBasico.Id);

        // Autenticar como SuperAdmin
        AuthenticateAs(usuarioId: 1, comercioId: 0, role: "SuperAdmin");

        // ── Act 1: Crear trial ──
        var createRequest = new { ruc = "0991234567001", razonSocial = "Comercio Test SA" };
        var createResponse = await Client.PostAsJsonAsync("/api/admin/trials", createRequest);

        // Assert 1: Respuesta 201 con datos correctos
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var trialResult = await createResponse.Content.ReadFromJsonAsync<TrialResultDto>(JsonOptions);
        Assert.NotNull(trialResult);
        Assert.True(trialResult.ComercioId > 0);
        Assert.True(trialResult.SuscripcionId > 0);
        Assert.Equal("Comercio Test SA", trialResult.RazonSocial);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), trialResult.FechaInicio);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15), trialResult.FechaProximoCorte);

        // Verificar estado en BD
        await using var verifyCtx1 = CreateDbContext();
        var comercio = await verifyCtx1.Comercios
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == trialResult.ComercioId);
        Assert.Equal("Activo", comercio.Estado);

        var suscripcion = await verifyCtx1.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == trialResult.SuscripcionId);
        Assert.Equal("Trial", suscripcion.Estado);
        Assert.Equal(planBasico.Id, suscripcion.PlanId);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15), suscripcion.FechaProximoCorte);

        // ── Act 2: Extender trial por 5 días ──
        var extendRequest = new { diasAdicionales = 5 };
        var extendResponse = await Client.PostAsJsonAsync(
            $"/api/admin/trials/{trialResult.SuscripcionId}/extender", extendRequest);

        // Assert 2: Extensión exitosa con nueva fecha
        Assert.Equal(HttpStatusCode.OK, extendResponse.StatusCode);
        var extensionResult = await extendResponse.Content.ReadFromJsonAsync<ExtensionResultDto>(JsonOptions);
        Assert.NotNull(extensionResult);

        var fechaEsperadaExtendida = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20); // 15 + 5
        Assert.Equal(fechaEsperadaExtendida, extensionResult.NuevaFechaProximoCorte);

        // Verificar en BD que se actualizó correctamente
        await using var verifyCtx2 = CreateDbContext();
        var suscripcionExtendida = await verifyCtx2.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == trialResult.SuscripcionId);
        Assert.Equal(fechaEsperadaExtendida, suscripcionExtendida.FechaProximoCorte);
        Assert.Equal(5, suscripcionExtendida.DiasExtendidos);

        // ── Act 3: Simular expiración con BillingService.ProcesarCortesDiariosAsync ──
        await using var billingCtx = CreateDbContext();
        var billingService = CreateBillingService(billingCtx);

        // Avanzar el reloj al día de FechaProximoCorte para provocar la expiración
        var fechaExpiracion = fechaEsperadaExtendida.ToDateTime(TimeOnly.MinValue);
        await billingService.ProcesarCortesDiariosAsync(fechaExpiracion);

        // Assert 3: Verificar que la suscripción expiró y el comercio se suspendió
        await using var verifyCtx3 = CreateDbContext();
        var suscripcionExpirada = await verifyCtx3.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == trialResult.SuscripcionId);
        Assert.Equal("Trial_Expirado", suscripcionExpirada.Estado);

        var comercioSuspendido = await verifyCtx3.Comercios
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == trialResult.ComercioId);
        Assert.Equal("Suspendido", comercioSuspendido.Estado);

        // ── Act 4: Convertir trial expirado a suscripción paga ──
        var convertRequest = new { planId = planBasico.Id };
        var convertResponse = await Client.PostAsJsonAsync(
            $"/api/admin/trials/{trialResult.SuscripcionId}/convertir", convertRequest);

        // Assert 4: Conversión exitosa con reactivación
        Assert.Equal(HttpStatusCode.OK, convertResponse.StatusCode);
        var conversionResult = await convertResponse.Content.ReadFromJsonAsync<ConversionResultDto>(JsonOptions);
        Assert.NotNull(conversionResult);
        Assert.Equal("Básico", conversionResult.NombrePlan);
        Assert.Equal(350m, conversionResult.MontoCuota);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), conversionResult.FechaProximoCorte);

        // Verificar reactivación en BD
        await using var verifyCtx4 = CreateDbContext();
        var suscripcionConvertida = await verifyCtx4.Suscripciones
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == trialResult.SuscripcionId);
        Assert.Equal("Activa", suscripcionConvertida.Estado);

        var comercioReactivado = await verifyCtx4.Comercios
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == trialResult.ComercioId);
        Assert.Equal("Activo", comercioReactivado.Estado);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 2: Atomicidad de la transacción
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifica que al intentar crear un trial con RUC duplicado no queden datos parciales.
    /// Primero crea un comercio existente, luego intenta crear trial con mismo RUC
    /// y confirma que no se crearon registros adicionales.
    /// </summary>
    [DockerAvailableFact]
    public async Task CreateTrial_DuplicateRuc_NoPartialDataRemains()
    {
        // Arrange: seed Plan Básico y un Comercio existente con el mismo RUC
        await using var seedCtx = CreateDbContext();
        var plan = new Plan
        {
            Nombre = "Básico",
            Precio = 350m,
            LimiteUsuarios = 5,
            LimiteAtributos = 3
        };
        seedCtx.Planes.Add(plan);
        await seedCtx.SaveChangesAsync();

        var comercioExistente = new Comercio
        {
            Ruc = "0997654321001",
            RazonSocial = "Comercio Existente",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow
        };
        seedCtx.Comercios.Add(comercioExistente);
        await seedCtx.SaveChangesAsync();

        // Contar registros antes del intento
        var comerciosAntes = await seedCtx.Comercios.IgnoreQueryFilters().CountAsync();
        var suscripcionesAntes = await seedCtx.Suscripciones.IgnoreQueryFilters().CountAsync();

        AuthenticateAs(usuarioId: 1, comercioId: 0, role: "SuperAdmin");

        // Act: intentar crear trial con RUC duplicado
        var createRequest = new { ruc = "0997654321001", razonSocial = "Comercio Duplicado" };
        var response = await Client.PostAsJsonAsync("/api/admin/trials", createRequest);

        // Assert: respuesta 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Verificar que NO se crearon registros parciales
        await using var verifyCtx = CreateDbContext();
        var comerciosDespues = await verifyCtx.Comercios.IgnoreQueryFilters().CountAsync();
        var suscripcionesDespues = await verifyCtx.Suscripciones.IgnoreQueryFilters().CountAsync();

        Assert.Equal(comerciosAntes, comerciosDespues);
        Assert.Equal(suscripcionesAntes, suscripcionesDespues);

        // Verificar que no existe un comercio con la razón social del intento fallido
        var comercioDuplicado = await verifyCtx.Comercios
            .IgnoreQueryFilters()
            .AnyAsync(c => c.RazonSocial == "Comercio Duplicado");
        Assert.False(comercioDuplicado);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 3: Middleware bloquea comercio suspendido
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifica que el TenantContextMiddleware retorna 403 para un comercio
    /// con Estado="Suspendido" al intentar acceder a rutas /api/tenants/.
    /// </summary>
    [DockerAvailableFact]
    public async Task SuspendedComercio_AccessTenantRoute_Returns403()
    {
        // Arrange: crear comercio suspendido directamente en BD
        await using var seedCtx = CreateDbContext();
        var plan = new Plan
        {
            Nombre = "Básico",
            Precio = 350m,
            LimiteUsuarios = 5,
            LimiteAtributos = 3
        };
        seedCtx.Planes.Add(plan);
        await seedCtx.SaveChangesAsync();

        var comercioSuspendido = new Comercio
        {
            Ruc = "0998765432001",
            RazonSocial = "Comercio Suspendido",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Estado = "Suspendido",
            FechaRegistro = DateTime.UtcNow
        };
        seedCtx.Comercios.Add(comercioSuspendido);
        await seedCtx.SaveChangesAsync();

        // Seed usuario 100 en DB para satisfacer JtiValidationMiddleware
        await seedCtx.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Usuarios"" (""Id"", ""ComercioId"", ""Nombre"", ""Email"", ""PasswordHash"", ""Rol"", ""Activo"")
              VALUES (100, {0}, 'Gerente Test', 'gerente@test.com', '$2a$12$dummyhashgerentevalue', 'Gerente', true)
              ON CONFLICT (""Id"") DO NOTHING",
            comercioSuspendido.Id);

        // Autenticar como usuario del comercio suspendido (rol Gerente)
        AuthenticateAs(
            usuarioId: 100,
            comercioId: comercioSuspendido.Id,
            role: "Gerente");

        // Act: intentar acceder a ruta de tenant
        var response = await Client.GetAsync("/api/tenants/productos");

        // Assert: respuesta 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("suspendido", body, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 4: Performance de validación de RUC
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifica que la validación de RUC (incluyendo búsqueda de duplicados)
    /// responde en menos de 2 segundos, incluso con un RUC duplicado.
    /// </summary>
    [DockerAvailableFact]
    public async Task CreateTrial_DuplicateRucValidation_RespondsWithin2Seconds()
    {
        // Arrange: seed Plan Básico y un comercio existente
        await using var seedCtx = CreateDbContext();
        var plan = new Plan
        {
            Nombre = "Básico",
            Precio = 350m,
            LimiteUsuarios = 5,
            LimiteAtributos = 3
        };
        seedCtx.Planes.Add(plan);
        await seedCtx.SaveChangesAsync();

        var comercioExistente = new Comercio
        {
            Ruc = "0991111111001",
            RazonSocial = "Comercio Performance",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow
        };
        seedCtx.Comercios.Add(comercioExistente);
        await seedCtx.SaveChangesAsync();

        AuthenticateAs(usuarioId: 1, comercioId: 0, role: "SuperAdmin");

        // Act: medir tiempo de respuesta con RUC duplicado
        var stopwatch = Stopwatch.StartNew();
        var response = await Client.PostAsJsonAsync("/api/admin/trials",
            new { ruc = "0991111111001", razonSocial = "Intento duplicado" });
        stopwatch.Stop();

        // Assert: respuesta en menos de 2 segundos
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"La validación de RUC tardó {stopwatch.ElapsedMilliseconds}ms, excediendo el límite de 2000ms");

        // Verificar que la respuesta es 409 (confirma que la validación se ejecutó)
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea una instancia de BillingService con sus dependencias reales para tests directos.
    /// </summary>
    private static BillingService CreateBillingService(Infrastructure.Data.AppDbContext context)
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

    /// <summary>
    /// Seed idempotente de Plan, Comercio sistema y Usuario SuperAdmin (Id=1).
    /// Llamado desde InitializeAsync para satisfacer JtiValidationMiddleware.
    /// Usa raw SQL con ON CONFLICT para evitar duplicados entre tests.
    /// </summary>
    private async Task SeedBaseDataAsync()
    {
        await using var db = CreateDbContext();

        // Plan base requerido por FK Comercios.PlanId
        // Usa nombre "Plan Sistema" para no interferir con el plan "Básico" que busca TrialService
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Planes"" (""Id"", ""Nombre"", ""Precio"", ""LimiteUsuarios"", ""LimiteAtributos"", ""LimiteSucursales"")
              VALUES (999, 'Plan Sistema', 350, 5, 3, 1)
              ON CONFLICT (""Id"") DO NOTHING");

        // Avanzar secuencia de Planes para que auto-increment no colisione con Id=999
        await db.Database.ExecuteSqlRawAsync(
            @"SELECT setval(pg_get_serial_sequence('""Planes""', 'Id'), GREATEST((SELECT MAX(""Id"") FROM ""Planes""), 999))");

        // Comercio sistema para SuperAdmin
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Comercios"" (""Ruc"", ""RazonSocial"", ""PlanId"", ""UsaFacturacionSRI"", ""Estado"", ""FechaRegistro"")
              VALUES ('0000000000001', 'Sistema (SuperAdmin)', 999, false, 'Activo', NOW())
              ON CONFLICT (""Ruc"") DO NOTHING");

        // Obtener Id del comercio sistema
        var comercioId = await db.Comercios
            .IgnoreQueryFilters()
            .Where(c => c.Ruc == "0000000000001")
            .Select(c => c.Id)
            .FirstAsync();

        // Usuario SuperAdmin (Id=1) para tests que usan AuthenticateAs(1, 0, "SuperAdmin")
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Usuarios"" (""Id"", ""ComercioId"", ""Nombre"", ""Email"", ""PasswordHash"", ""Rol"", ""Activo"")
              VALUES (1, {0}, 'SuperAdmin Test', 'superadmin@test.com', '$2a$12$dummyhashsuperadminvalue', 'SuperAdmin', true)
              ON CONFLICT (""Id"") DO NOTHING",
            comercioId);

        // Avanzar secuencia de Usuarios para evitar conflictos
        await db.Database.ExecuteSqlRawAsync(
            @"SELECT setval(pg_get_serial_sequence('""Usuarios""', 'Id'), GREATEST((SELECT MAX(""Id"") FROM ""Usuarios""), 100))");
    }

    /// <summary>
    /// Seed un comercio "sistema" y un usuario SuperAdmin con Id=1.
    /// Idempotente: usa ON CONFLICT para Comercio y Usuario.
    /// Requerido por Test 1 que necesita el usuario asociado al plan que crea.
    /// </summary>
    private static async Task SeedSuperAdminUser(Infrastructure.Data.AppDbContext context, int planId)
    {
        // Insertar comercio sistema de forma idempotente (ON CONFLICT por Ruc)
        await context.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Comercios"" (""Ruc"", ""RazonSocial"", ""PlanId"", ""UsaFacturacionSRI"", ""Estado"", ""FechaRegistro"")
              VALUES ('0000000000001', 'Sistema (SuperAdmin)', {0}, false, 'Activo', NOW())
              ON CONFLICT (""Ruc"") DO NOTHING",
            planId);

        // Obtener Id del comercio sistema
        var comercioId = await context.Comercios
            .IgnoreQueryFilters()
            .Where(c => c.Ruc == "0000000000001")
            .Select(c => c.Id)
            .FirstAsync();

        // Insertar con Id explícito=1 para que coincida con el JWT claim sub=1
        await context.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Usuarios"" (""Id"", ""ComercioId"", ""Nombre"", ""Email"", ""PasswordHash"", ""Rol"", ""Activo"")
              VALUES (1, {0}, 'SuperAdmin Test', 'superadmin@test.com', '$2a$12$dummyhashsuperadminvalue', 'SuperAdmin', true)
              ON CONFLICT (""Id"") DO NOTHING",
            comercioId);

        // Avanzar la secuencia para evitar conflictos futuros
        await context.Database.ExecuteSqlRawAsync(
            @"SELECT setval(pg_get_serial_sequence('""Usuarios""', 'Id'), GREATEST((SELECT MAX(""Id"") FROM ""Usuarios""), 1))");
    }
}
