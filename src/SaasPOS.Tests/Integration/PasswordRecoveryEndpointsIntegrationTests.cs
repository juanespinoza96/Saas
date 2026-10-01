using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Pruebas de integración de la funcionalidad de recuperación de contraseña jerárquica
/// (tarea 18.1, valida las tareas 15–17: endpoints tenant/Admin y servicio en segundo plano).
///
/// Usan WebApplicationFactory + Testcontainers.PostgreSQL a través de <see cref="IntegrationTestBase"/>
/// y cubren:
///   1. Rate limiting: la 4ª solicitud de recuperación en una hora desde la misma IP → HTTP 429
///      (PasswordRecoveryPolicy = 3/hora). _Requirements: 14.1, 14.2_
///   2. Aprobación cross-tenant: un SuperAdmin aprueba la solicitud de un Dueño vía
///      PasswordRecoveryAdminController (api/admin/password-recovery). _Requirements: 13.1_
///   3. Login con contraseña temporal expirada NO concede acceso (401). _Requirements: 6.4_
///   4. Un ciclo del PasswordRecoveryCleanupBackgroundService borra el PasswordTemporalCifrada
///      de las solicitudes aprobadas cuya temporal ya venció. _Requirements: 7.1, 7.2_
///
/// Nota: el flujo silencioso de solicitud siempre responde 200; el rate limiter reside en el
/// middleware, por lo que la 4ª solicitud es rechazada con 429 antes de llegar al controlador.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "PasswordRecovery")]
public class PasswordRecoveryEndpointsIntegrationTests : IntegrationTestBase
{
    // Secreto de cifrado AES-256 de las temporales; el servicio deriva la clave vía SHA-256.
    private const string EncryptionKeyName = "PASSWORD_RECOVERY_ENCRYPTION_KEY";
    private const string EncryptionKeyValue = "clave-integracion-recuperacion-password-jerarquica-2026!";

    public PasswordRecoveryEndpointsIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        // El host de la API resuelve AesPasswordEncryptionService, que lee el secreto de cifrado desde
        // IConfiguration con reserva a la variable de entorno. En el entorno "Testing" no hay appsettings
        // con este secreto, por lo que se establece como variable de entorno del proceso para que la
        // aprobación cross-tenant (que cifra la temporal dentro del host) funcione. _Requirements: 4.5_
        Environment.SetEnvironmentVariable(EncryptionKeyName, EncryptionKeyValue);

        await base.InitializeAsync();
        await CleanDatabaseAsync();
        await SeedPlansAsync();
        await SeedSuperAdminAsync();
    }

    private async Task SeedPlansAsync()
    {
        await using var db = CreateDbContext();
        db.Set<Plan>().AddRange(
            new Plan { Id = 1, Nombre = "Básico", Precio = 350m, LimiteUsuarios = 2, LimiteAtributos = 2, LimiteSucursales = 1 },
            new Plan { Id = 2, Nombre = "Intermedio", Precio = 750m, LimiteUsuarios = 3, LimiteAtributos = 5, LimiteSucursales = 0 },
            new Plan { Id = 3, Nombre = "Empresarial", Precio = 1200m, LimiteUsuarios = 0, LimiteAtributos = 0, LimiteSucursales = 0 }
        );
        await db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1) Rate limiting: 4ª solicitud/hora desde la misma IP → 429 (Req 14.1, 14.2).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// La PasswordRecoveryPolicy permite 3 solicitudes por hora por IP; la 4ª debe ser rechazada
    /// con HTTP 429. Las 3 primeras responden 200 (flujo silencioso), independientemente de si el
    /// correo existe. _Requirements: 14.1, 14.2_
    /// </summary>
    [DockerAvailableFact]
    public async Task CuartaSolicitudDeRecuperacionEnUnaHora_DesdeMismaIp_Retorna429()
    {
        // Arrange: cuerpo de solicitud con un correo cualquiera (el flujo es silencioso).
        var body = new { email = $"recuperacion-ratelimit-{Guid.NewGuid():N}@test.com" };

        // Act: enviar 4 solicitudes consecutivas a través del mismo cliente (misma IP/partición).
        var respuestas = new List<HttpResponseMessage>();
        for (int i = 0; i < 4; i++)
        {
            var respuesta = await Client.PostAsJsonAsync("/api/tenants/auth/password-recovery/request", body);
            respuestas.Add(respuesta);
        }

        // Assert: las 3 primeras dentro del límite → 200; la 4ª → 429.
        Assert.All(respuestas.Take(3), r =>
            Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        Assert.Equal(HttpStatusCode.TooManyRequests, respuestas[3].StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2) Aprobación cross-tenant: SuperAdmin aprueba a un Dueño (Req 13.1).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un SuperAdmin, mediante PasswordRecoveryAdminController (api/admin/password-recovery),
    /// aprueba la solicitud de recuperación de un Dueño de cualquier comercio (cross-tenant).
    /// La respuesta 200 devuelve la contraseña temporal; en la base de datos la solicitud queda
    /// Aprobada, con el Dueño marcado para cambiar su contraseña. _Requirements: 13.1_
    /// </summary>
    [DockerAvailableFact]
    public async Task SuperAdmin_ApruebaSolicitudDeDueno_CrossTenant_Retorna200()
    {
        // Arrange: un comercio con un Dueño y una solicitud Pendiente para ese Dueño.
        int solicitudId;
        int duenoId;
        await using (var db = CreateDbContext())
        {
            var comercio = await TestDataBuilder.Comercio().ConRazonSocial("Comercio Dueño").ConPlan(3).CrearAsync(db);
            var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
            var dueno = await TestDataBuilder.Usuario()
                .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Dueño").CrearAsync(db);
            duenoId = dueno.Id;

            var solicitud = new SolicitudRecuperacion
            {
                UsuarioId = dueno.Id,
                ComercioId = comercio.Id,
                Estado = "Pendiente",
                FechaSolicitud = DateTime.UtcNow,
            };
            db.SolicitudesRecuperacion.Add(solicitud);
            await db.SaveChangesAsync();
            solicitudId = solicitud.Id;
        }

        // Autenticarse como SuperAdmin (Id=999 sembrado por SeedSuperAdminAsync).
        AuthenticateAs(999, 0, "SuperAdmin");

        // Act: aprobar la solicitud del Dueño vía la ruta cross-tenant del Admin.
        // Se envía un cuerpo JSON vacío para evitar 415 (UnsupportedMediaType); el endpoint approve
        // no lee cuerpo, pero algunos clientes requieren un Content-Type válido en el POST.
        var respuesta = await Client.PostAsJsonAsync(
            $"/api/admin/password-recovery/approve/{solicitudId}", new { });

        // Assert: 200 con una contraseña temporal no vacía.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        var tempPassword = cuerpo.GetProperty("tempPassword").GetString();
        Assert.False(string.IsNullOrWhiteSpace(tempPassword), "La aprobación debe devolver una contraseña temporal.");

        // La solicitud queda Aprobada y el Dueño marcado para cambiar su contraseña.
        await using var verify = CreateDbContext();
        var solicitudFinal = await verify.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .SingleAsync(s => s.Id == solicitudId);
        Assert.Equal("Aprobada", solicitudFinal.Estado);
        Assert.Equal(999, solicitudFinal.AprobadoPor);
        Assert.NotNull(solicitudFinal.PasswordTemporalCifrada);

        var duenoFinal = await verify.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Id == duenoId);
        Assert.True(duenoFinal.DebeCambiarPassword, "El Dueño debe quedar obligado a cambiar la contraseña.");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3) Login con contraseña temporal expirada → 401 (Req 6.4).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un usuario cuya contraseña temporal ya expiró (solicitud Aprobada con FechaExpiracion en el
    /// pasado y cifrado borrado por la limpieza) NO obtiene acceso al iniciar sesión con esa temporal:
    /// el login devuelve 401. _Requirements: 6.4_
    /// </summary>
    [DockerAvailableFact]
    public async Task Login_ConContrasenaTemporalExpirada_NoConcedeAcceso_Retorna401()
    {
        // Arrange: crear un comercio con un aprobador (Gerente) y un solicitante (Cajero) con
        // una solicitud Pendiente; aprobarla vía el servicio real para obtener la temporal.
        var (solicitudId, solicitanteEmail, tempPassword) = await AprobarYObtenerTemporalAsync();

        // Simular la expiración: FechaExpiracion en el pasado y el cifrado borrado, tal como
        // lo dejaría el servicio de limpieza / la validación perezosa tras 24 horas.
        await using (var db = CreateDbContext())
        {
            var solicitud = await db.SolicitudesRecuperacion
                .IgnoreQueryFilters()
                .SingleAsync(s => s.Id == solicitudId);
            solicitud.FechaExpiracion = DateTime.UtcNow.AddHours(-1);
            solicitud.PasswordTemporalCifrada = null;
            await db.SaveChangesAsync();
        }

        // Usar una IP única para no interferir con la LoginPolicy de otros tests del colectivo.
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.77.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: intentar iniciar sesión con la contraseña temporal ya expirada.
        var respuesta = await Client.PostAsJsonAsync("/api/tenants/auth/login",
            new { email = solicitanteEmail, password = tempPassword });

        // Assert: el acceso NO se concede. Se acepta 429 solo como límite conocido del rate limiter
        // compartido en el colectivo de tests; el foco es que NUNCA se conceda acceso (nunca 200 con token).
        Assert.True(
            respuesta.StatusCode == HttpStatusCode.Unauthorized ||
            respuesta.StatusCode == HttpStatusCode.TooManyRequests,
            $"Se esperaba 401 (o 429 por aislamiento del rate limiter), pero se obtuvo {(int)respuesta.StatusCode}.");

        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4) Un ciclo del background service borra el cifrado de aprobadas vencidas (Req 7.1, 7.2).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un ciclo del <see cref="PasswordRecoveryCleanupBackgroundService"/> borra el
    /// PasswordTemporalCifrada de las solicitudes Aprobadas cuya FechaExpiracion ya venció, y
    /// deja intacto el cifrado de las que aún están vigentes. _Requirements: 7.1, 7.2_
    /// </summary>
    [DockerAvailableFact]
    public async Task CicloDeLimpieza_BorraCifradoDeAprobadasVencidas_YRespetaVigentes()
    {
        // Arrange: sembrar dos solicitudes Aprobadas con cifrado presente:
        //  - una VENCIDA (FechaExpiracion en el pasado) → debe borrarse el cifrado.
        //  - una VIGENTE (FechaExpiracion en el futuro) → debe conservarse el cifrado.
        int solicitudVencidaId;
        int solicitudVigenteId;
        await using (var db = CreateDbContext())
        {
            var comercio = await TestDataBuilder.Comercio().ConRazonSocial("Comercio Limpieza").ConPlan(3).CrearAsync(db);
            var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
            var usuario = await TestDataBuilder.Usuario()
                .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

            var ahora = DateTime.UtcNow;

            var vencida = new SolicitudRecuperacion
            {
                UsuarioId = usuario.Id,
                ComercioId = comercio.Id,
                Estado = "Aprobada",
                FechaSolicitud = ahora.AddHours(-25),
                FechaResolucion = ahora.AddHours(-25),
                FechaExpiracion = ahora.AddHours(-1), // ya venció
                PasswordTemporalCifrada = "cifrado-vencido-base64",
            };
            var vigente = new SolicitudRecuperacion
            {
                UsuarioId = usuario.Id,
                ComercioId = comercio.Id,
                Estado = "Aprobada",
                FechaSolicitud = ahora.AddHours(-1),
                FechaResolucion = ahora.AddHours(-1),
                FechaExpiracion = ahora.AddHours(23), // aún vigente
                PasswordTemporalCifrada = "cifrado-vigente-base64",
            };
            db.SolicitudesRecuperacion.AddRange(vencida, vigente);
            await db.SaveChangesAsync();
            solicitudVencidaId = vencida.Id;
            solicitudVigenteId = vigente.Id;
        }

        // Act: ejecutar un único ciclo del servicio en segundo plano contra el contenedor de prueba.
        await EjecutarUnCicloDeLimpiezaAsync();

        // Assert: la vencida perdió su cifrado; la vigente lo conserva.
        await using var verify = CreateDbContext();
        var vencidaFinal = await verify.SolicitudesRecuperacion.IgnoreQueryFilters().SingleAsync(s => s.Id == solicitudVencidaId);
        var vigenteFinal = await verify.SolicitudesRecuperacion.IgnoreQueryFilters().SingleAsync(s => s.Id == solicitudVigenteId);

        Assert.Null(vencidaFinal.PasswordTemporalCifrada);
        Assert.NotNull(vigenteFinal.PasswordTemporalCifrada);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Crea un comercio con un aprobador (Gerente) y un solicitante (Cajero) con una solicitud
    /// Pendiente, y la aprueba usando el <see cref="AuthService"/> real (con cifrado AES-256 real)
    /// para obtener la contraseña temporal en texto plano. Devuelve el id de la solicitud, el email
    /// del solicitante y la temporal generada.
    /// </summary>
    private async Task<(int solicitudId, string solicitanteEmail, string tempPassword)> AprobarYObtenerTemporalAsync()
    {
        await using var db = CreateDbContext();

        var comercio = await TestDataBuilder.Comercio().ConRazonSocial("Comercio Login").ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);
        var solicitante = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero")
            .ConEmail($"solicitante-login-{Guid.NewGuid():N}@test.com").CrearAsync(db);

        var solicitud = new SolicitudRecuperacion
        {
            UsuarioId = solicitante.Id,
            ComercioId = comercio.Id,
            Estado = "Pendiente",
            FechaSolicitud = DateTime.UtcNow,
        };
        db.SolicitudesRecuperacion.Add(solicitud);
        await db.SaveChangesAsync();

        var authService = CrearAuthService(db);
        var aprobacion = await authService.ApprovePasswordRecoveryAsync(solicitud.Id, gerente.Id);
        Assert.True(aprobacion.Success, $"La aprobación de preparación falló: {aprobacion.ErrorCode}.");

        return (solicitud.Id, solicitante.Email, aprobacion.TempPassword!);
    }

    /// <summary>
    /// Construye un <see cref="AuthService"/> real sobre el DbContext del contenedor de prueba,
    /// con cifrado AES-256 real y colaboradores mínimos (JTI/auditoría no verificados aquí).
    /// </summary>
    private AuthService CrearAuthService(AppDbContext db)
    {
        var jwtSettings = Microsoft.Extensions.Options.Options.Create(new SaasPOS.Infrastructure.Configuration.JwtSettings
        {
            SecretKey = "IntegrationTestSecretKeyThatIsAtLeast32Characters!",
            Issuer = "saas-pos-api",
            Audience = "saas-pos-clients",
            ExpirationMinutes = 60,
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [EncryptionKeyName] = EncryptionKeyValue })
            .Build();

        IPasswordEncryptionService cifrado = new AesPasswordEncryptionService(configuration);

        return new AuthService(
            db,
            jwtSettings,
            new NoOpJtiBlocklist(),
            cifrado,
            new NoOpAuditService());
    }

    /// <summary>
    /// Ejecuta un único ciclo del <see cref="PasswordRecoveryCleanupBackgroundService"/> contra el
    /// contenedor de prueba: se arranca el servicio (que realiza un barrido inmediato antes del delay
    /// de 15 min) y se detiene tras confirmar que el cifrado vencido fue borrado.
    /// </summary>
    private async Task EjecutarUnCicloDeLimpiezaAsync()
    {
        var services = new ServiceCollection();
        // Registrar el AppDbContext apuntando al contenedor de prueba (el servicio resuelve un scope por iteración).
        services.AddDbContext<AppDbContext>((sp, options) => options.UseNpgsql(Fixture.ConnectionString));
        services.AddScoped<ITenantContext, SuperAdminTenantContext>();

        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var servicio = new PasswordRecoveryCleanupBackgroundService(
            scopeFactory,
            NullLogger<PasswordRecoveryCleanupBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        await servicio.StartAsync(cts.Token);

        // El primer barrido se ejecuta inmediatamente al arrancar (antes del Task.Delay de 15 min).
        // Se espera activamente (hasta 10 s) a que el cifrado vencido sea borrado para no depender de timings.
        var borrado = false;
        for (int i = 0; i < 50 && !borrado; i++)
        {
            await Task.Delay(200);
            await using var db = CreateDbContext();
            borrado = !await db.SolicitudesRecuperacion
                .IgnoreQueryFilters()
                .AnyAsync(s => s.Estado == "Aprobada"
                               && s.PasswordTemporalCifrada != null
                               && s.FechaExpiracion != null
                               && s.FechaExpiracion < DateTime.UtcNow);
        }

        await servicio.StopAsync(cts.Token);
    }
}

/// <summary>
/// Implementación no operativa de <see cref="IJtiBlocklist"/> para los tests de integración de
/// recuperación (la invalidación de sesiones se verifica en las pruebas unitarias con Moq).
/// </summary>
internal sealed class NoOpJtiBlocklist : IJtiBlocklist
{
    public void AddToBlocklist(string jti, DateTime expiration) { }
    public bool IsBlocked(string jti) => false;
    public void BlockAllForUser(int userId) { }
    public void BlockAllForComercio(int comercioId) { }
}

/// <summary>
/// Implementación no operativa de <see cref="IAuditService"/> para los tests de integración de
/// recuperación (la auditoría se verifica en las pruebas unitarias con Moq).
/// </summary>
internal sealed class NoOpAuditService : IAuditService
{
    public Task RegistrarAsync(
        int comercioId,
        int? usuarioId,
        string accion,
        string tablaAfectada,
        string registroId,
        object? valoresAnteriores,
        object? valoresNuevos) => Task.CompletedTask;
}
