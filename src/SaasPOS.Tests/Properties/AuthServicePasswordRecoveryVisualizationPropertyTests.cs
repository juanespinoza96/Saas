using FsCheck;
using FsCheck.Xunit;
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

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Feature: recuperacion-password-jerarquica, Properties 6, 7 y 8.
///
/// Pruebas de propiedad sobre <see cref="AuthService"/> centradas en la visualización de la
/// contraseña temporal, su expiración/borrado perezoso y el tratamiento de una solicitud
/// Pendiente vencida como Expirada. Se usa una base de datos EF Core InMemory por caso y un
/// servicio de cifrado AES real (round-trip fiel); JTI y auditoría se sustituyen por mocks.
///
/// **Validates: Requirements 5.1, 5.2, 5.4, 6.1, 6.2, 6.3, 7.2, 7.3, 7.4**
/// </summary>
public class AuthServicePasswordRecoveryVisualizationPropertyTests
{
    // Ventana de vigencia del flujo de recuperación (24 horas), igual que la constante interna del servicio.
    private const int RecoveryExpirationHours = 24;

    private static readonly JwtSettings TestJwtSettings = new()
    {
        SecretKey = "ThisIsATestSecretKeyThatIsLongEnoughForHmacSha256Algorithm!!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpirationMinutes = 60,
    };

    // Clave de cifrado de prueba: el servicio deriva la clave AES-256 vía SHA-256, cualquier valor no vacío sirve.
    private const string EncryptionKeyName = "PASSWORD_RECOVERY_ENCRYPTION_KEY";
    private const string EncryptionKeyValue = "clave-de-prueba-para-cifrado-de-contrasena-temporal-visualizacion";

    // Pares (aprobador, solicitante) del mismo comercio donde el aprobador SÍ puede resolver la solicitud.
    // Se evita SuperAdmin para no entrar en la rama cross-tenant; aquí interesa el contexto POS.
    private static readonly (string Aprobador, string Solicitante)[] ParesAutorizadosMismoComercio =
    {
        ("Dueño", "Gerente"),
        ("Dueño", "Supervisor"),
        ("Dueño", "Bodeguero"),
        ("Dueño", "Cajero"),
        ("Gerente", "Supervisor"),
        ("Gerente", "Bodeguero"),
        ("Gerente", "Cajero"),
    };

    private static Gen<(string Aprobador, string Solicitante)> GenParAutorizado() =>
        Gen.Elements(ParesAutorizadosMismoComercio);

    /// <summary>
    /// Crea un servicio de cifrado AES real, de modo que el descifrado devuelva exactamente la
    /// contraseña temporal original almacenada al aprobar.
    /// </summary>
    private static IPasswordEncryptionService CrearServicioCifrado()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EncryptionKeyName] = EncryptionKeyValue,
            })
            .Build();

        return new AesPasswordEncryptionService(configuration);
    }

    /// <summary>
    /// Construye un <see cref="AuthService"/> sobre una base InMemory aislada, con cifrado AES real
    /// y mocks para el JTI blocklist y la auditoría (no relevantes para estas propiedades).
    /// </summary>
    private static AuthService CrearAuthService(AppDbContext dbContext, IPasswordEncryptionService cifrado)
    {
        var jwtOptions = Options.Create(TestJwtSettings);
        var jtiBlocklistMock = new Mock<IJtiBlocklist>();
        var auditServiceMock = new Mock<IAuditService>();

        return new AuthService(
            dbContext,
            jwtOptions,
            jtiBlocklistMock.Object,
            cifrado,
            auditServiceMock.Object);
    }

    /// <summary>
    /// Crea una base de datos EF Core InMemory con un nombre único por caso, con el filtro de tenant
    /// desactivado (ITenantContext.IsSuperAdmin = true) para no interferir con los inserts de prueba.
    /// </summary>
    private static AppDbContext CrearDbContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantContextMock.Object);
    }

    /// <summary>
    /// Inserta un aprobador y un solicitante del mismo comercio y devuelve sus IDs.
    /// </summary>
    private static (int AprobadorId, int SolicitanteId) SembrarUsuarios(
        AppDbContext db, int comercioId, string rolAprobador, string rolSolicitante)
    {
        var aprobador = new Usuario
        {
            ComercioId = comercioId,
            Nombre = "Aprobador",
            Email = $"aprobador-{Guid.NewGuid():N}@test.com",
            PasswordHash = AuthService.HashPassword("Passw0rd@X"),
            Rol = rolAprobador,
            Activo = true,
        };
        var solicitante = new Usuario
        {
            ComercioId = comercioId,
            Nombre = "Solicitante",
            Email = $"solicitante-{Guid.NewGuid():N}@test.com",
            PasswordHash = AuthService.HashPassword("Passw0rd@Y"),
            Rol = rolSolicitante,
            Activo = true,
        };

        db.Usuarios.AddRange(aprobador, solicitante);
        db.SaveChanges();

        return (aprobador.Id, solicitante.Id);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Property 6: Visualización idempotente e ilimitada mientras la temporal es vigente.
    // _Requirements: 5.1, 5.2_
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 6: Mientras la contraseña temporal de una solicitud Aprobada sigue vigente, un
    /// aprobador autorizado puede reconsultarla un número ilimitado de veces y SIEMPRE obtiene la
    /// misma contraseña descifrada, sin que el estado de la solicitud ni el cifrado almacenado cambien
    /// (visualización idempotente).
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property6_VisualizacionIdempotenteEIlimitada_MientrasVigente()
    {
        var gen =
            from par in GenParAutorizado()
            // Número de reconsultas: entre 2 y 20 para ejercitar la idempotencia ilimitada.
            from repeticiones in Gen.Choose(2, 20)
            select (par, repeticiones);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            var ((rolAprobador, rolSolicitante), repeticiones) = input;

            using var db = CrearDbContext();
            var cifrado = CrearServicioCifrado();
            var authService = CrearAuthService(db, cifrado);

            const int comercioId = 100;
            var (aprobadorId, solicitanteId) = SembrarUsuarios(db, comercioId, rolAprobador, rolSolicitante);

            // Crear la solicitud Pendiente recién creada y aprobarla para generar la temporal vigente.
            var solicitud = new SolicitudRecuperacion
            {
                UsuarioId = solicitanteId,
                ComercioId = comercioId,
                Estado = "Pendiente",
                FechaSolicitud = DateTime.UtcNow,
            };
            db.SolicitudesRecuperacion.Add(solicitud);
            db.SaveChanges();

            var aprobacion = authService.ApprovePasswordRecoveryAsync(solicitud.Id, aprobadorId)
                .GetAwaiter().GetResult();

            // Precondición: la aprobación debe haber sido exitosa y entregado la temporal.
            if (!aprobacion.Success || string.IsNullOrEmpty(aprobacion.TempPassword))
                return false.Label($"La aprobación falló ({aprobacion.ErrorCode}); no se puede probar la visualización.");

            var esperada = aprobacion.TempPassword;

            // Reconsultar la temporal varias veces: todas deben devolver éxito con la MISMA contraseña.
            for (var i = 0; i < repeticiones; i++)
            {
                var consulta = authService.GetTempPasswordAsync(solicitud.Id, aprobadorId)
                    .GetAwaiter().GetResult();

                if (!consulta.Success)
                    return false.Label($"La reconsulta #{i + 1} de {repeticiones} falló con {consulta.ErrorCode}.");

                if (!string.Equals(consulta.TempPassword, esperada, StringComparison.Ordinal))
                    return false.Label($"La reconsulta #{i + 1} devolvió una contraseña distinta a la de la aprobación.");
            }

            // El estado y el cifrado no deben haber cambiado tras las reconsultas (idempotencia).
            var recargada = db.SolicitudesRecuperacion.IgnoreQueryFilters().First(s => s.Id == solicitud.Id);
            var estadoIntacto = string.Equals(recargada.Estado, "Aprobada", StringComparison.Ordinal);
            var cifradoIntacto = recargada.PasswordTemporalCifrada != null;

            return (estadoIntacto && cifradoIntacto)
                .Label($"Tras {repeticiones} reconsultas: estado='{recargada.Estado}', cifradoPresente={cifradoIntacto}.");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Property 7: Una temporal expirada nunca se entrega descifrada y su cifrado se borra.
    // _Requirements: 6.2, 6.3, 7.2, 7.3, 7.4, 5.4_
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 7: Para cualquier solicitud Aprobada cuya FechaExpiracion ya pasó, la reconsulta de la
    /// contraseña temporal nunca la entrega descifrada (falla con código Gone) y, además, la validación
    /// perezosa borra el valor cifrado almacenado (PasswordTemporalCifrada queda en null).
    ///
    /// **Validates: Requirements 6.2, 6.3, 7.2, 7.3, 7.4, 5.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property7_TemporalExpirada_NoSeEntregaYSeBorraElCifrado()
    {
        var gen =
            from par in GenParAutorizado()
            // Minutos transcurridos desde la expiración: entre 1 y 10080 (una semana) → siempre vencida.
            from minutosVencida in Gen.Choose(1, 7 * 24 * 60)
            select (par, minutosVencida);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            var ((rolAprobador, rolSolicitante), minutosVencida) = input;

            using var db = CrearDbContext();
            var cifrado = CrearServicioCifrado();
            var authService = CrearAuthService(db, cifrado);

            const int comercioId = 200;
            var (aprobadorId, solicitanteId) = SembrarUsuarios(db, comercioId, rolAprobador, rolSolicitante);

            var ahora = DateTime.UtcNow;

            // Solicitud Aprobada con la contraseña temporal YA cifrada pero con FechaExpiracion en el pasado.
            // Se cifra un valor real para asegurar que, de entregarse, sería descifrable: la propiedad exige
            // que aun así NO se entregue por estar vencida.
            var solicitud = new SolicitudRecuperacion
            {
                UsuarioId = solicitanteId,
                ComercioId = comercioId,
                Estado = "Aprobada",
                AprobadoPor = aprobadorId,
                // La solicitud se resolvió hace más de 24h; la expiración quedó en el pasado.
                FechaSolicitud = ahora.AddHours(-RecoveryExpirationHours - 1),
                FechaResolucion = ahora.AddMinutes(-minutosVencida - (RecoveryExpirationHours * 60)),
                FechaExpiracion = ahora.AddMinutes(-minutosVencida),
                PasswordTemporalCifrada = cifrado.Encrypt("Temp0ral@Vencida"),
            };
            db.SolicitudesRecuperacion.Add(solicitud);
            db.SaveChanges();

            var consulta = authService.GetTempPasswordAsync(solicitud.Id, aprobadorId)
                .GetAwaiter().GetResult();

            // No debe entregar la temporal: falla con Gone (410) y sin contraseña.
            var noEntregada = !consulta.Success
                && consulta.ErrorCode == RecoveryErrorCode.Gone
                && string.IsNullOrEmpty(consulta.TempPassword);

            // La validación perezosa debe haber borrado el cifrado almacenado.
            var recargada = db.SolicitudesRecuperacion.IgnoreQueryFilters().First(s => s.Id == solicitud.Id);
            var cifradoBorrado = recargada.PasswordTemporalCifrada == null;

            return (noEntregada && cifradoBorrado)
                .Label($"Vencida hace {minutosVencida} min: entregada={consulta.Success} " +
                       $"({consulta.ErrorCode}), cifradoBorrado={cifradoBorrado}.");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Property 8: Una solicitud Pendiente vencida se trata como Expirada.
    // _Requirements: 6.1, 7.3, 7.4_
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 8: Para cualquier solicitud que sigue en estado Pendiente pero cuya FechaSolicitud es
    /// anterior a hace 24 horas, la validación perezosa la trata como Expirada: al intentar aprobarla,
    /// el servicio la rechaza con código Conflict (409) y no genera contraseña temporal ni altera la
    /// solicitud (sigue Pendiente, sin cifrado). Además, una Pendiente vencida no aparece en la bandeja.
    ///
    /// **Validates: Requirements 6.1, 7.3, 7.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property8_PendienteVencida_SeTrataComoExpirada()
    {
        var gen =
            from par in GenParAutorizado()
            // Minutos por encima del corte de 24h: entre 1 y 10080 → siempre vencida.
            from minutosPasadoCorte in Gen.Choose(1, 7 * 24 * 60)
            select (par, minutosPasadoCorte);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            var ((rolAprobador, rolSolicitante), minutosPasadoCorte) = input;

            using var db = CrearDbContext();
            var cifrado = CrearServicioCifrado();
            var authService = CrearAuthService(db, cifrado);

            const int comercioId = 300;
            var (aprobadorId, solicitanteId) = SembrarUsuarios(db, comercioId, rolAprobador, rolSolicitante);

            // Solicitud Pendiente cuya FechaSolicitud está más allá del corte de 24h (vencida).
            var solicitud = new SolicitudRecuperacion
            {
                UsuarioId = solicitanteId,
                ComercioId = comercioId,
                Estado = "Pendiente",
                FechaSolicitud = DateTime.UtcNow.AddHours(-RecoveryExpirationHours).AddMinutes(-minutosPasadoCorte),
            };
            db.SolicitudesRecuperacion.Add(solicitud);
            db.SaveChanges();

            // Aprobar una Pendiente vencida debe rechazarse con Conflict (tratada como Expirada).
            var aprobacion = authService.ApprovePasswordRecoveryAsync(solicitud.Id, aprobadorId)
                .GetAwaiter().GetResult();

            var rechazadaComoConflicto = !aprobacion.Success
                && aprobacion.ErrorCode == RecoveryErrorCode.Conflict
                && string.IsNullOrEmpty(aprobacion.TempPassword);

            // La solicitud no debe haber cambiado: sigue Pendiente y sin contraseña temporal cifrada.
            var recargada = db.SolicitudesRecuperacion.IgnoreQueryFilters().First(s => s.Id == solicitud.Id);
            var solicitudIntacta = string.Equals(recargada.Estado, "Pendiente", StringComparison.Ordinal)
                && recargada.PasswordTemporalCifrada == null;

            // Una Pendiente vencida tampoco debe aparecer en la bandeja del aprobador (validación perezosa).
            var bandeja = authService.GetPendingRecoveriesAsync(aprobadorId).GetAwaiter().GetResult();
            var ausenteEnBandeja = bandeja.All(d => d.RequestId != solicitud.Id);

            return (rechazadaComoConflicto && solicitudIntacta && ausenteEnBandeja)
                .Label($"Vencida +{minutosPasadoCorte} min: aprobación={aprobacion.ErrorCode}, " +
                       $"estado='{recargada.Estado}', ausenteEnBandeja={ausenteEnBandeja}.");
        });
    }
}
