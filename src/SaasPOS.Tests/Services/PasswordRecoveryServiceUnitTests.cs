using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using SaasPOS.Application.DTOs.Results;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;
using Xunit;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Feature: recuperacion-password-jerarquica, tarea 14.4.
/// Unit tests (xUnit + Moq) sobre la lógica de servicio de <see cref="AuthService"/> para la
/// recuperación de contraseña jerárquica. A diferencia de las pruebas basadas en propiedades
/// (que validan invariantes sobre muchos inputs), estos tests verifican con Moq.Verify los
/// efectos colaterales concretos exigidos por los requisitos:
///
///   - <c>InvalidateSessionAsync</c> se invoca con el id del solicitante al aprobar (Req 4.7).
///   - Se registra auditoría de aprobación (aprobador, solicitante, timestamp) al aprobar (Req 15.1).
///   - Se registra auditoría de rechazo con el motivo al rechazar (Req 15.4).
///   - <c>FechaSolicitud</c> se establece con la marca de tiempo al crear la solicitud (Req 1.5).
///   - El flujo <c>request</c> responde 200 (servicio devuelve true) con email existente e inexistente (Req 1.1).
///
/// Cada test construye un <see cref="AuthService"/> real sobre EF Core InMemory (aislado por Guid),
/// con cifrado AES-256 real y mocks de <see cref="IJtiBlocklist"/> e <see cref="IAuditService"/>
/// para poder verificar las interacciones.
/// </summary>
public class PasswordRecoveryServiceUnitTests
{
    // Secreto de cifrado AES-256 en memoria (el servicio deriva la clave vía SHA-256; cualquier valor no vacío sirve).
    private const string EncryptionKeyName = "PASSWORD_RECOVERY_ENCRYPTION_KEY";
    private const string EncryptionKeyValue = "clave-de-prueba-unit-tests-recuperacion-password-32bytes+";

    // Contraseña conocida que cumple la Politica_Password (mayúscula, dígito, especial permitido).
    private const string KnownValidPassword = "Abc-123";

    private static readonly JwtSettings TestJwtSettings = new()
    {
        SecretKey = "ThisIsATestSecretKeyThatIsLongEnoughForHmacSha256Algorithm!!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpirationMinutes = 60,
    };

    /// <summary>
    /// Contexto de prueba autocontenido: el <see cref="AppDbContext"/> InMemory, el
    /// <see cref="AuthService"/> bajo prueba y los mocks para verificar efectos colaterales.
    /// </summary>
    private sealed class ContextoPrueba : IDisposable
    {
        public required AppDbContext Db { get; init; }
        public required AuthService Servicio { get; init; }
        public required Mock<IJtiBlocklist> JtiBlocklist { get; init; }
        public required Mock<IAuditService> AuditService { get; init; }

        public void Dispose() => Db.Dispose();
    }

    // Construye un AuthService real sobre EF Core InMemory con cifrado AES-256 real y mocks de JTI y auditoría.
    // El ITenantContext se configura como SuperAdmin para no interferir con los filtros de tenant
    // (el servicio ya usa IgnoreQueryFilters internamente en el flujo de recuperación).
    private static ContextoPrueba CrearContexto()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EncryptionKeyName] = EncryptionKeyValue,
            })
            .Build();

        IPasswordEncryptionService cifrado = new AesPasswordEncryptionService(configuration);

        var jtiBlocklist = new Mock<IJtiBlocklist>();
        var auditService = new Mock<IAuditService>();

        var servicio = new AuthService(
            db,
            Options.Create(TestJwtSettings),
            jtiBlocklist.Object,
            cifrado,
            auditService.Object);

        return new ContextoPrueba
        {
            Db = db,
            Servicio = servicio,
            JtiBlocklist = jtiBlocklist,
            AuditService = auditService,
        };
    }

    // Crea y persiste un usuario activo con el rol, comercio y contraseña conocida indicados.
    private static Usuario SembrarUsuario(AppDbContext db, string rol, int comercioId, string emailPrefix)
    {
        var usuario = new Usuario
        {
            ComercioId = comercioId,
            Nombre = $"Usuario {emailPrefix}",
            Email = $"{emailPrefix}-{Guid.NewGuid():N}@test.com",
            PasswordHash = AuthService.HashPassword(KnownValidPassword),
            Rol = rol,
            Activo = true,
        };
        db.Usuarios.Add(usuario);
        db.SaveChanges();
        return usuario;
    }

    // Crea y persiste una SolicitudRecuperacion Pendiente reciente (no vencida) para el solicitante indicado.
    private static SolicitudRecuperacion SembrarSolicitudPendiente(AppDbContext db, Usuario solicitante)
    {
        var solicitud = new SolicitudRecuperacion
        {
            UsuarioId = solicitante.Id,
            ComercioId = solicitante.ComercioId,
            Estado = "Pendiente",
            FechaSolicitud = DateTime.UtcNow,
        };
        db.SolicitudesRecuperacion.Add(solicitud);
        db.SaveChanges();
        return solicitud;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 4.7: InvalidateSessionAsync se invoca con el id del solicitante al aprobar.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Al aprobar una solicitud, el servicio invalida todas las sesiones activas del solicitante.
    /// <c>InvalidateSessionAsync</c> delega en <c>IJtiBlocklist.BlockAllForUser</c>, por lo que se
    /// verifica con Moq que se invoca exactamente una vez con el id del solicitante (y no con el del
    /// aprobador). **Validates: Requirements 4.7**
    /// </summary>
    [Fact]
    public async Task Aprobar_InvalidaSesionesDelSolicitante_ConSuId()
    {
        using var ctx = CrearContexto();
        const int comercioId = 100;

        var aprobador = SembrarUsuario(ctx.Db, "Gerente", comercioId, "aprobador");
        var solicitante = SembrarUsuario(ctx.Db, "Cajero", comercioId, "solicitante");
        var solicitud = SembrarSolicitudPendiente(ctx.Db, solicitante);

        var resultado = await ctx.Servicio.ApprovePasswordRecoveryAsync(solicitud.Id, aprobador.Id);

        Assert.True(resultado.Success);
        // Se invalidan las sesiones del SOLICITANTE (no del aprobador), exactamente una vez.
        ctx.JtiBlocklist.Verify(j => j.BlockAllForUser(solicitante.Id), Times.Once);
        ctx.JtiBlocklist.Verify(j => j.BlockAllForUser(aprobador.Id), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 15.1: Auditoría de aprobación con aprobador, solicitante y timestamp.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Al aprobar, se registra un evento de auditoría con la acción "AprobarRecuperacionPassword",
    /// asociado al ComercioId del solicitante y al usuarioId del aprobador. Se verifica con Moq que
    /// <c>RegistrarAsync</c> se invoca una vez con esos parámetros clave. **Validates: Requirements 15.1**
    /// </summary>
    [Fact]
    public async Task Aprobar_RegistraAuditoriaDeAprobacion()
    {
        using var ctx = CrearContexto();
        const int comercioId = 200;

        var aprobador = SembrarUsuario(ctx.Db, "Dueño", comercioId, "aprobador");
        var solicitante = SembrarUsuario(ctx.Db, "Gerente", comercioId, "solicitante");
        var solicitud = SembrarSolicitudPendiente(ctx.Db, solicitante);

        var resultado = await ctx.Servicio.ApprovePasswordRecoveryAsync(solicitud.Id, aprobador.Id);

        Assert.True(resultado.Success);
        // Evento de auditoría: acción de aprobación, comercio del solicitante, usuario = aprobador,
        // tabla y registro afectados correctos. El timestamp se registra dentro de valoresNuevos.
        ctx.AuditService.Verify(a => a.RegistrarAsync(
            solicitante.ComercioId,
            aprobador.Id,
            "AprobarRecuperacionPassword",
            "SolicitudesRecuperacion",
            solicitud.Id.ToString(),
            It.IsAny<object?>(),
            It.Is<object?>(v => v != null)),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 15.4: Auditoría de rechazo con el motivo.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Al rechazar con un motivo, se registra un evento de auditoría con la acción
    /// "RechazarRecuperacionPassword". Se verifica con Moq que <c>RegistrarAsync</c> se invoca una
    /// vez con el comercio del solicitante, el aprobador, la tabla/registro correctos y que el
    /// objeto de valores nuevos contiene el motivo de rechazo. **Validates: Requirements 15.4**
    /// </summary>
    [Fact]
    public async Task Rechazar_ConMotivo_RegistraAuditoriaConElMotivo()
    {
        using var ctx = CrearContexto();
        const int comercioId = 300;
        const string motivo = "El correo no corresponde al empleado actual.";

        var aprobador = SembrarUsuario(ctx.Db, "Dueño", comercioId, "aprobador");
        var solicitante = SembrarUsuario(ctx.Db, "Cajero", comercioId, "solicitante");
        var solicitud = SembrarSolicitudPendiente(ctx.Db, solicitante);

        var resultado = await ctx.Servicio.RejectPasswordRecoveryAsync(solicitud.Id, aprobador.Id, motivo);

        Assert.True(resultado.Success);
        // Evento de auditoría de rechazo: acción correcta, comercio del solicitante, usuario = aprobador,
        // y el motivo presente en el objeto de valores nuevos (se comprueba vía reflexión sobre la propiedad).
        ctx.AuditService.Verify(a => a.RegistrarAsync(
            solicitante.ComercioId,
            aprobador.Id,
            "RechazarRecuperacionPassword",
            "SolicitudesRecuperacion",
            solicitud.Id.ToString(),
            It.IsAny<object?>(),
            It.Is<object?>(v => ContieneMotivo(v, motivo))),
            Times.Once);
    }

    /// <summary>
    /// Comprueba, vía reflexión, que el objeto anónimo de "valores nuevos" del log de auditoría de
    /// rechazo contiene una propiedad <c>MotivoRechazo</c> con el motivo esperado. Esto valida que
    /// la auditoría del rechazo incluye efectivamente el motivo (Req 15.4).
    /// </summary>
    private static bool ContieneMotivo(object? valoresNuevos, string motivoEsperado)
    {
        if (valoresNuevos is null)
            return false;

        var propiedad = valoresNuevos.GetType().GetProperty("MotivoRechazo");
        if (propiedad is null)
            return false;

        return (propiedad.GetValue(valoresNuevos) as string) == motivoEsperado;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 1.5: FechaSolicitud se establece con la marca de tiempo al crear la solicitud.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Al crear una SolicitudRecuperacion para un usuario existente, la <c>FechaSolicitud</c> se
    /// establece con la marca de tiempo del instante de creación (dentro de la ventana [antes, después]).
    /// **Validates: Requirements 1.5**
    /// </summary>
    [Fact]
    public async Task Request_UsuarioExistente_EstableceFechaSolicitud()
    {
        using var ctx = CrearContexto();
        var usuario = SembrarUsuario(ctx.Db, "Cajero", 400, "solicitante");

        var antes = DateTime.UtcNow;
        var resultado = await ctx.Servicio.RequestPasswordRecoveryAsync(usuario.Email);
        var despues = DateTime.UtcNow;

        Assert.True(resultado);

        var solicitud = ctx.Db.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .Single(s => s.UsuarioId == usuario.Id);

        // La FechaSolicitud debe corresponder al instante de creación.
        Assert.InRange(solicitud.FechaSolicitud, antes, despues);
        Assert.Equal("Pendiente", solicitud.Estado);
        Assert.Equal(usuario.ComercioId, solicitud.ComercioId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 1.1: El flujo request responde 200 (servicio devuelve true) con email existente e inexistente.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Con un email que corresponde a un usuario existente, el flujo silencioso responde de forma
    /// exitosa (el servicio devuelve true, que la capa API mapea a 200) y crea la solicitud.
    /// **Validates: Requirements 1.1**
    /// </summary>
    [Fact]
    public async Task Request_EmailExistente_RespondeOkYCreaSolicitud()
    {
        using var ctx = CrearContexto();
        var usuario = SembrarUsuario(ctx.Db, "Cajero", 500, "existente");

        var resultado = await ctx.Servicio.RequestPasswordRecoveryAsync(usuario.Email);

        Assert.True(resultado);
        var total = ctx.Db.SolicitudesRecuperacion.IgnoreQueryFilters().Count(s => s.UsuarioId == usuario.Id);
        Assert.Equal(1, total);
    }

    /// <summary>
    /// Con un email que NO corresponde a ningún usuario, el flujo silencioso responde igualmente de
    /// forma exitosa (el servicio devuelve true → 200) sin crear ninguna solicitud, para no revelar
    /// la ausencia del correo. **Validates: Requirements 1.1**
    /// </summary>
    [Fact]
    public async Task Request_EmailInexistente_RespondeOkSinCrearSolicitud()
    {
        using var ctx = CrearContexto();
        // No se siembra ningún usuario con este correo.
        var emailInexistente = $"noexiste-{Guid.NewGuid():N}@test.com";

        var resultado = await ctx.Servicio.RequestPasswordRecoveryAsync(emailInexistente);

        Assert.True(resultado);
        var total = ctx.Db.SolicitudesRecuperacion.IgnoreQueryFilters().Count();
        Assert.Equal(0, total);
    }
}
