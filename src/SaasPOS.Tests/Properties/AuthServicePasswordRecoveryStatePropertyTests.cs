using System.IdentityModel.Tokens.Jwt;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using SaasPOS.Application.DTOs.Results;
using SaasPOS.Application.Helpers;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Pruebas basadas en propiedades (FsCheck.Xunit, ≥100 iteraciones) para la lógica de estado
/// del flujo de recuperación de contraseña jerárquica implementado en <see cref="AuthService"/>.
///
/// Cubre las propiedades de la tarea 14.3:
///   - Property 9:  Creación silenciosa condicionada sin alterar el hash.
///   - Property 10: No duplicación de solicitudes activas.
///   - Property 12: Correspondencia entre la bandera de cambio y el claim del JWT.
///   - Property 14: El cambio definitivo de contraseña deja el estado consistente.
///   - Property 15: Rechazo requiere motivo y produce transición consistente.
///   - Property 16: Cambio voluntario correcto sin efectos de recuperación.
///
/// Cada prueba construye un <see cref="AuthService"/> real sobre una base de datos EF Core
/// InMemory aislada (nombre único por caso), con un <see cref="IPasswordEncryptionService"/>
/// AES-256 real y mocks de <see cref="IJtiBlocklist"/> e <see cref="IAuditService"/>.
/// </summary>
public class AuthServicePasswordRecoveryStatePropertyTests
{
    // ── Configuración compartida de prueba ──

    // Secreto de cifrado AES-256 (el servicio deriva la clave vía SHA-256; cualquier valor no vacío sirve).
    private const string EncryptionKeyName = "PASSWORD_RECOVERY_ENCRYPTION_KEY";
    private const string EncryptionKeyValue = "clave-de-prueba-para-cifrado-de-contrasena-temporal-32bytes+";

    private static readonly JwtSettings TestJwtSettings = new()
    {
        SecretKey = "ThisIsATestSecretKeyThatIsLongEnoughForHmacSha256Algorithm!!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpirationMinutes = 60,
    };

    // Jerarquía estricta del dominio (mayor = más autoridad) para construir pares aprobador/solicitante válidos.
    private static readonly (string Rol, int Rango)[] RolesConRango =
    {
        ("SuperAdmin", 5),
        ("Dueño", 4),
        ("Gerente", 3),
        ("Supervisor", 2),
        ("Bodeguero", 1),
        ("Cajero", 0),
    };

    /// <summary>
    /// Crea un <see cref="AppDbContext"/> InMemory aislado (nombre único) junto con las dependencias
    /// de <see cref="AuthService"/>: un servicio de cifrado AES real y mocks de JTI y auditoría.
    /// El <see cref="ITenantContext"/> se configura como SuperAdmin para no interferir con los filtros
    /// de tenant (el servicio ya usa IgnoreQueryFilters internamente).
    /// </summary>
    private static (AuthService Service, AppDbContext Db, Mock<IJtiBlocklist> Jti, Mock<IAuditService> Audit)
        CrearServicio()
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

        var encryption = new AesPasswordEncryptionService(configuration);
        var jtiMock = new Mock<IJtiBlocklist>();
        var auditMock = new Mock<IAuditService>();

        var service = new AuthService(
            db,
            Options.Create(TestJwtSettings),
            jtiMock.Object,
            encryption,
            auditMock.Object);

        return (service, db, jtiMock, auditMock);
    }

    // Contraseña conocida que siempre cumple la Politica_Password (mayúscula, dígito, especial permitido).
    private const string KnownValidPassword = "Abc-123";

    /// <summary>Inserta un usuario activo con la contraseña conocida y devuelve la entidad persistida.</summary>
    private static Usuario SeedUsuario(AppDbContext db, int comercioId, string rol, string emailPrefix, bool debeCambiar = false)
    {
        var usuario = new Usuario
        {
            ComercioId = comercioId,
            Nombre = "Usuario " + emailPrefix,
            Email = $"{emailPrefix}@test.com",
            PasswordHash = AuthService.HashPassword(KnownValidPassword),
            Rol = rol,
            Activo = true,
            DebeCambiarPassword = debeCambiar,
        };
        db.Usuarios.Add(usuario);
        db.SaveChanges();
        return usuario;
    }

    /// <summary>Sanitiza un prefijo generado para formar un identificador de email seguro y no vacío.</summary>
    private static string SanitizarPrefijo(string prefijo)
    {
        var limpio = new string((prefijo ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(limpio) ? "user" : limpio;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Feature: recuperacion-password-jerarquica, Property 9:
    // Creación silenciosa condicionada sin alterar el hash.
    //
    // Para cualquier usuario existente sin Solicitud_Activa, RequestPasswordRecoveryAsync crea
    // exactamente una SolicitudRecuperacion Pendiente asociada a su UsuarioId/ComercioId; para un
    // correo inexistente no crea nada; en ningún caso modifica el PasswordHash del usuario.
    //
    // **Validates: Requirements 1.2, 1.3, 1.4**
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Correo existente sin solicitud activa: se crea una única solicitud Pendiente con el UsuarioId
    /// y ComercioId correctos, y el PasswordHash del usuario permanece intacto (Req 1.2, 1.4).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Solicitud_UsuarioExistente_CreaPendienteSinTocarHash(
        PositiveInt comercioIdGen, NonEmptyString emailGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var prefijo = SanitizarPrefijo(emailGen.Get);

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            var usuario = SeedUsuario(db, comercioId, "Cajero", prefijo);
            var hashOriginal = usuario.PasswordHash;

            service.RequestPasswordRecoveryAsync(usuario.Email).GetAwaiter().GetResult();

            var solicitudes = db.SolicitudesRecuperacion
                .IgnoreQueryFilters()
                .Where(s => s.UsuarioId == usuario.Id)
                .ToList();

            var hashActual = db.Usuarios.IgnoreQueryFilters().First(u => u.Id == usuario.Id).PasswordHash;

            var creoUnaPendiente = solicitudes.Count == 1
                && solicitudes[0].Estado == "Pendiente"
                && solicitudes[0].ComercioId == comercioId;
            var hashIntacto = hashActual == hashOriginal;

            return (creoUnaPendiente && hashIntacto)
                .Label($"creoUnaPendiente={creoUnaPendiente} (count={solicitudes.Count}), hashIntacto={hashIntacto}");
        }
    }

    /// <summary>
    /// Correo inexistente: no se crea ninguna SolicitudRecuperacion (Req 1.3).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Solicitud_CorreoInexistente_NoCreaNada(NonEmptyString emailGen)
    {
        var prefijo = SanitizarPrefijo(emailGen.Get);
        var emailInexistente = $"noexiste-{prefijo}@test.com";

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            service.RequestPasswordRecoveryAsync(emailInexistente).GetAwaiter().GetResult();

            var total = db.SolicitudesRecuperacion.IgnoreQueryFilters().Count();
            return (total == 0).Label($"No debe crear solicitudes para correo inexistente, pero hay {total}.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Feature: recuperacion-password-jerarquica, Property 10:
    // No duplicación de solicitudes activas.
    //
    // Varias solicitudes consecutivas para un usuario con una Solicitud_Activa (Pendiente no vencida)
    // no producen solicitudes adicionales; si la previa venció (>24h), se permite crear una nueva.
    //
    // **Validates: Requirements 2.1, 2.2, 2.3**
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Con una solicitud Pendiente vigente, N solicitudes adicionales no crean duplicados:
    /// sigue existiendo exactamente una solicitud activa (Req 2.1, 2.2).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Solicitud_ConActivaVigente_NoDuplica(PositiveInt repeticionesGen, NonEmptyString emailGen)
    {
        // Entre 2 y 6 solicitudes repetidas.
        var repeticiones = (repeticionesGen.Get % 5) + 2;
        var prefijo = SanitizarPrefijo(emailGen.Get);

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            var usuario = SeedUsuario(db, 7, "Cajero", prefijo);

            for (var i = 0; i < repeticiones; i++)
            {
                service.RequestPasswordRecoveryAsync(usuario.Email).GetAwaiter().GetResult();
            }

            var total = db.SolicitudesRecuperacion.IgnoreQueryFilters().Count(s => s.UsuarioId == usuario.Id);
            return (total == 1)
                .Label($"Tras {repeticiones} solicitudes debe existir 1 sola, pero hay {total}.");
        }
    }

    /// <summary>
    /// Si la única solicitud previa está vencida (FechaSolicitud &gt; 24h) o resuelta, una nueva
    /// solicitud sí se crea (Req 2.3). Se fuerza el vencimiento retrocediendo la FechaSolicitud.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Solicitud_ConPreviaVencida_PermiteNueva(PositiveInt horasExtraGen, NonEmptyString emailGen)
    {
        // Entre 25 y 48 horas de antigüedad → la previa está vencida (ventana de 24h).
        var horas = (horasExtraGen.Get % 24) + 25;
        var prefijo = SanitizarPrefijo(emailGen.Get);

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            var usuario = SeedUsuario(db, 9, "Cajero", prefijo);

            // Solicitud Pendiente pero ya vencida (más de 24h de antigüedad).
            db.SolicitudesRecuperacion.Add(new SolicitudRecuperacion
            {
                UsuarioId = usuario.Id,
                ComercioId = usuario.ComercioId,
                Estado = "Pendiente",
                FechaSolicitud = DateTime.UtcNow.AddHours(-horas),
            });
            db.SaveChanges();

            service.RequestPasswordRecoveryAsync(usuario.Email).GetAwaiter().GetResult();

            // Debe existir al menos una Pendiente vigente (creada ahora), además de la vencida.
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var vigentes = db.SolicitudesRecuperacion.IgnoreQueryFilters()
                .Count(s => s.UsuarioId == usuario.Id && s.Estado == "Pendiente" && s.FechaSolicitud > cutoff);

            return (vigentes == 1)
                .Label($"Debe crearse una nueva solicitud vigente tras vencer la previa, pero vigentes={vigentes}.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Feature: recuperacion-password-jerarquica, Property 12:
    // Correspondencia entre la bandera de cambio y el claim del JWT.
    //
    // El claim must_change_password aparece en el JWT emitido por LoginAsync si y solo si
    // el usuario tiene DebeCambiarPassword activo.
    //
    // **Validates: Requirements 8.1, 8.2, 8.3**
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Para cualquier valor de la bandera DebeCambiarPassword, el login exitoso produce un JWT
    /// que contiene el claim must_change_password exactamente cuando la bandera está activa (Req 8.1, 8.2, 8.3).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Login_ClaimMustChange_CorrespondeConLaBandera(bool debeCambiar, NonEmptyString emailGen)
    {
        var prefijo = SanitizarPrefijo(emailGen.Get);

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            var usuario = SeedUsuario(db, 3, "Cajero", prefijo, debeCambiar: debeCambiar);

            var result = service.LoginAsync(usuario.Email, KnownValidPassword).GetAwaiter().GetResult();
            if (!result.Success || string.IsNullOrEmpty(result.Token))
                return false.Label("El login con credenciales válidas debería tener éxito.");

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
            var claim = jwt.Claims.FirstOrDefault(c => c.Type == "must_change_password");

            // El claim debe estar presente con valor "true" sii la bandera está activa.
            var claimPresenteYVerdadero = claim != null
                && string.Equals(claim.Value, "true", StringComparison.OrdinalIgnoreCase);

            return (claimPresenteYVerdadero == debeCambiar)
                .Label($"debeCambiar={debeCambiar}, claimPresente={claim != null}, valor={claim?.Value ?? "<null>"}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Feature: recuperacion-password-jerarquica, Property 14:
    // El cambio definitivo de contraseña deja el estado consistente.
    //
    // Tras aprobar una solicitud y ejecutar ChangePasswordAsync con una nueva contraseña válida:
    // el PasswordHash queda rotado (BCrypt WF≥12 que verifica la nueva), DebeCambiarPassword=false,
    // y el cifrado de la temporal (PasswordTemporalCifrada) queda borrado.
    //
    // **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 9.3**
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un ciclo aprobar → cambiar contraseña deja: BCrypt.Verify(nueva)=true con WF≥12,
    /// DebeCambiarPassword=false y PasswordTemporalCifrada=null en la solicitud asociada (Req 10.1–10.4, 9.3).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CambioObligatorio_DejaEstadoConsistente(NonEmptyString emailGen)
    {
        var prefijo = SanitizarPrefijo(emailGen.Get);
        // Nueva contraseña definitiva válida distinta de la temporal generada.
        const string nuevaPassword = "Definitiva-9X";

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            var comercioId = 5;
            var solicitante = SeedUsuario(db, comercioId, "Cajero", prefijo + "sol");
            var aprobador = SeedUsuario(db, comercioId, "Gerente", prefijo + "apr");

            var solicitud = new SolicitudRecuperacion
            {
                UsuarioId = solicitante.Id,
                ComercioId = comercioId,
                Estado = "Pendiente",
                FechaSolicitud = DateTime.UtcNow,
            };
            db.SolicitudesRecuperacion.Add(solicitud);
            db.SaveChanges();

            // Aprobar genera la temporal, rota el hash y activa la bandera.
            var approve = service.ApprovePasswordRecoveryAsync(solicitud.Id, aprobador.Id).GetAwaiter().GetResult();
            if (!approve.Success)
                return false.Label($"La aprobación debería tener éxito. Error={approve.ErrorCode}.");

            // Cambio de contraseña definitivo.
            var change = service.ChangePasswordAsync(solicitante.Id, nuevaPassword).GetAwaiter().GetResult();
            if (!change.Success)
                return false.Label($"El cambio de contraseña debería tener éxito. Error={change.ErrorCode}.");

            var usuarioFinal = db.Usuarios.IgnoreQueryFilters().First(u => u.Id == solicitante.Id);
            var solicitudFinal = db.SolicitudesRecuperacion.IgnoreQueryFilters().First(s => s.Id == solicitud.Id);

            var verificaNueva = BCrypt.Net.BCrypt.Verify(nuevaPassword, usuarioFinal.PasswordHash);
            var workFactorOk = ExtraerWorkFactor(usuarioFinal.PasswordHash) >= 12;
            var banderaDesactivada = usuarioFinal.DebeCambiarPassword == false;
            var temporalBorrada = solicitudFinal.PasswordTemporalCifrada == null;

            return (verificaNueva && workFactorOk && banderaDesactivada && temporalBorrada)
                .Label($"verificaNueva={verificaNueva}, wf>=12={workFactorOk}, " +
                       $"bandera={usuarioFinal.DebeCambiarPassword}, temporalBorrada={temporalBorrada}");
        }
    }

    /// <summary>Extrae el work factor de un hash BCrypt con formato $2a$NN$... (NN = work factor).</summary>
    private static int ExtraerWorkFactor(string bcryptHash)
    {
        // Formato: $2a$12$... → el work factor son los dos dígitos entre el 2º y 3er '$'.
        var partes = bcryptHash.Split('$');
        if (partes.Length >= 3 && int.TryParse(partes[2], out var wf))
            return wf;
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Feature: recuperacion-password-jerarquica, Property 15:
    // Rechazo requiere motivo y produce transición consistente.
    //
    // Un rechazo con motivo vacío/espacios no modifica la solicitud (MissingReason); un rechazo
    // con motivo por un aprobador autorizado deja Estado=Rechazada con MotivoRechazo, AprobadoPor
    // y FechaResolucion establecidos.
    //
    // **Validates: Requirements 15.2, 15.3, 15.5**
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rechazo con motivo en blanco (vacío o solo espacios): resultado MissingReason y la solicitud
    /// permanece Pendiente y sin motivo (Req 15.2).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Rechazo_MotivoEnBlanco_NoModificaSolicitud(NonNegativeInt espaciosGen, NonEmptyString emailGen)
    {
        var prefijo = SanitizarPrefijo(emailGen.Get);
        // Motivo compuesto solo por espacios en blanco (0 a 5 espacios).
        var motivo = new string(' ', espaciosGen.Get % 6);

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            var comercioId = 11;
            var solicitante = SeedUsuario(db, comercioId, "Cajero", prefijo + "sol");
            var aprobador = SeedUsuario(db, comercioId, "Gerente", prefijo + "apr");

            var solicitud = new SolicitudRecuperacion
            {
                UsuarioId = solicitante.Id,
                ComercioId = comercioId,
                Estado = "Pendiente",
                FechaSolicitud = DateTime.UtcNow,
            };
            db.SolicitudesRecuperacion.Add(solicitud);
            db.SaveChanges();

            var reject = service.RejectPasswordRecoveryAsync(solicitud.Id, aprobador.Id, motivo).GetAwaiter().GetResult();

            var solicitudFinal = db.SolicitudesRecuperacion.IgnoreQueryFilters().First(s => s.Id == solicitud.Id);

            var esMissingReason = !reject.Success && reject.ErrorCode == RecoveryErrorCode.MissingReason;
            var sinModificar = solicitudFinal.Estado == "Pendiente"
                && solicitudFinal.MotivoRechazo == null
                && solicitudFinal.AprobadoPor == null
                && solicitudFinal.FechaResolucion == null;

            return (esMissingReason && sinModificar)
                .Label($"missingReason={esMissingReason}, sinModificar={sinModificar}, estado={solicitudFinal.Estado}");
        }
    }

    /// <summary>
    /// Rechazo con motivo no vacío por un aprobador autorizado: transición a Rechazada con el
    /// MotivoRechazo almacenado, AprobadoPor y FechaResolucion establecidos (Req 15.3, 15.5).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Rechazo_ConMotivo_TransicionConsistente(NonEmptyString motivoGen, NonEmptyString emailGen)
    {
        var prefijo = SanitizarPrefijo(emailGen.Get);
        // Garantizar un motivo con contenido no-espacio.
        var motivo = "Motivo: " + motivoGen.Get.Trim();
        if (string.IsNullOrWhiteSpace(motivo))
            motivo = "Motivo de rechazo válido";

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            var comercioId = 13;
            var solicitante = SeedUsuario(db, comercioId, "Cajero", prefijo + "sol");
            var aprobador = SeedUsuario(db, comercioId, "Gerente", prefijo + "apr");

            var solicitud = new SolicitudRecuperacion
            {
                UsuarioId = solicitante.Id,
                ComercioId = comercioId,
                Estado = "Pendiente",
                FechaSolicitud = DateTime.UtcNow,
            };
            db.SolicitudesRecuperacion.Add(solicitud);
            db.SaveChanges();

            var antes = DateTime.UtcNow.AddSeconds(-1);
            var reject = service.RejectPasswordRecoveryAsync(solicitud.Id, aprobador.Id, motivo).GetAwaiter().GetResult();
            var despues = DateTime.UtcNow.AddSeconds(1);

            if (!reject.Success)
                return false.Label($"El rechazo con motivo debería tener éxito. Error={reject.ErrorCode}.");

            var solicitudFinal = db.SolicitudesRecuperacion.IgnoreQueryFilters().First(s => s.Id == solicitud.Id);

            var estadoOk = solicitudFinal.Estado == "Rechazada";
            var motivoOk = solicitudFinal.MotivoRechazo == motivo;
            var aprobadoPorOk = solicitudFinal.AprobadoPor == aprobador.Id;
            var fechaOk = solicitudFinal.FechaResolucion.HasValue
                && solicitudFinal.FechaResolucion.Value >= antes
                && solicitudFinal.FechaResolucion.Value <= despues;

            return (estadoOk && motivoOk && aprobadoPorOk && fechaOk)
                .Label($"estado={solicitudFinal.Estado}, motivoOk={motivoOk}, aprobadoPorOk={aprobadoPorOk}, fechaOk={fechaOk}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Feature: recuperacion-password-jerarquica, Property 16:
    // Cambio voluntario correcto sin efectos de recuperación.
    //
    // Con la contraseña actual correcta y una nueva contraseña válida, ChangePasswordVoluntaryAsync
    // rota el hash (BCrypt WF≥12 que verifica la nueva) sin activar DebeCambiarPassword ni generar
    // una contraseña temporal / SolicitudRecuperacion.
    //
    // **Validates: Requirements 17.2, 17.3, 17.6, 17.7**
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cambio voluntario correcto: hash rotado a la nueva (WF≥12), bandera de cambio inactiva y
    /// sin ninguna SolicitudRecuperacion generada (Req 17.2, 17.6, 17.7).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CambioVoluntario_Correcto_SinEfectosRecuperacion(NonEmptyString emailGen)
    {
        var prefijo = SanitizarPrefijo(emailGen.Get);
        const string nuevaPassword = "Voluntaria-7Z";

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            // Usuario con la bandera inicialmente inactiva (caso típico de cambio voluntario).
            var usuario = SeedUsuario(db, 21, "Cajero", prefijo, debeCambiar: false);

            var result = service.ChangePasswordVoluntaryAsync(usuario.Id, KnownValidPassword, nuevaPassword)
                .GetAwaiter().GetResult();

            if (!result.Success)
                return false.Label($"El cambio voluntario debería tener éxito. Error={result.ErrorCode}.");

            var usuarioFinal = db.Usuarios.IgnoreQueryFilters().First(u => u.Id == usuario.Id);
            var solicitudesGeneradas = db.SolicitudesRecuperacion.IgnoreQueryFilters().Count(s => s.UsuarioId == usuario.Id);

            var verificaNueva = BCrypt.Net.BCrypt.Verify(nuevaPassword, usuarioFinal.PasswordHash);
            var workFactorOk = ExtraerWorkFactor(usuarioFinal.PasswordHash) >= 12;
            var banderaInactiva = usuarioFinal.DebeCambiarPassword == false;
            var sinSolicitudes = solicitudesGeneradas == 0;

            return (verificaNueva && workFactorOk && banderaInactiva && sinSolicitudes)
                .Label($"verificaNueva={verificaNueva}, wf>=12={workFactorOk}, " +
                       $"bandera={usuarioFinal.DebeCambiarPassword}, sinSolicitudes={sinSolicitudes}");
        }
    }

    /// <summary>
    /// Cambio voluntario con contraseña actual incorrecta: resultado InvalidCredentials y el
    /// PasswordHash del usuario permanece intacto (Req 17.3).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CambioVoluntario_ActualIncorrecta_NoModificaHash(NonEmptyString emailGen, NonEmptyString actualGen)
    {
        var prefijo = SanitizarPrefijo(emailGen.Get);
        // Contraseña "actual" claramente distinta de la conocida.
        var actualIncorrecta = "Wrong-" + SanitizarPrefijo(actualGen.Get) + "9";
        const string nuevaPassword = "Voluntaria-7Z";

        var (service, db, _, _) = CrearServicio();
        using (db)
        {
            var usuario = SeedUsuario(db, 23, "Cajero", prefijo, debeCambiar: false);
            var hashOriginal = usuario.PasswordHash;

            // Si por casualidad coincidiera con la conocida, el caso deja de ser "incorrecta": se descarta.
            if (actualIncorrecta == KnownValidPassword)
                return true.ToProperty();

            var result = service.ChangePasswordVoluntaryAsync(usuario.Id, actualIncorrecta, nuevaPassword)
                .GetAwaiter().GetResult();

            var usuarioFinal = db.Usuarios.IgnoreQueryFilters().First(u => u.Id == usuario.Id);

            var esInvalidCredentials = !result.Success && result.ErrorCode == RecoveryErrorCode.InvalidCredentials;
            var hashIntacto = usuarioFinal.PasswordHash == hashOriginal;

            return (esInvalidCredentials && hashIntacto)
                .Label($"invalidCredentials={esInvalidCredentials}, hashIntacto={hashIntacto}");
        }
    }
}
