using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.DTOs.Results;
using SaasPOS.Application.Helpers;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Feature: recuperacion-password-jerarquica, Properties 1, 2 y 4:
/// Verificación de la lógica de servicio de <see cref="AuthService"/> para la aprobación
/// de solicitudes de recuperación (tarea 14.1). Estos tests ejercitan el servicio completo
/// contra un <see cref="AppDbContext"/> EF Core InMemory, con el cifrado AES-256 real y
/// dependencias mockeadas (JTI blocklist y auditoría), de modo que se validan los efectos
/// reales de la aprobación, el aislamiento multi-tenant y la matriz jerárquica.
///
/// Cada propiedad está etiquetada con el formato del diseño y enlaza sus requisitos.
/// </summary>
public class PasswordRecovery_Property1_2_4_ApprovalServiceTests
{
    // ── Datos de dominio compartidos ──

    // Roles válidos con su rango de autoridad (mayor = más autoridad). Tabla independiente de la
    // implementación, usada como fuente de verdad de la jerarquía en los generadores y oráculos.
    private static readonly (string Rol, int Rango)[] RolesConRango =
    {
        ("SuperAdmin", 5),
        ("Dueño", 4),
        ("Gerente", 3),
        ("Supervisor", 2),
        ("Bodeguero", 1),
        ("Cajero", 0),
    };

    // Roles que pueden actuar como aprobadores dentro del contexto POS (Req 3.6).
    private static readonly string[] RolesPos = { "Dueño", "Gerente" };

    // Secreto de cifrado AES-256 en memoria (mismo enfoque que Property 5): cualquier valor no vacío
    // sirve porque el servicio deriva la clave vía SHA-256.
    private const string ClaveNombre = "PASSWORD_RECOVERY_ENCRYPTION_KEY";
    private const string ClaveValor = "clave-de-prueba-aprobacion-recuperacion-password-32bytes+";

    private static readonly JwtSettings TestJwtSettings = new()
    {
        SecretKey = "ThisIsATestSecretKeyThatIsLongEnoughForHmacSha256Algorithm!!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpirationMinutes = 60,
    };

    // ── Infraestructura de pruebas ──

    /// <summary>
    /// Contexto de prueba autocontenido: un <see cref="AppDbContext"/> InMemory aislado por Guid,
    /// el <see cref="AuthService"/> bajo prueba y los mocks para verificar efectos colaterales.
    /// </summary>
    private sealed class ContextoPrueba : IDisposable
    {
        public required AppDbContext Db { get; init; }
        public required AuthService Servicio { get; init; }
        public required Mock<IJtiBlocklist> JtiBlocklist { get; init; }
        public required IPasswordEncryptionService Cifrado { get; init; }

        public void Dispose() => Db.Dispose();
    }

    // Construye un AuthService real sobre EF Core InMemory con cifrado AES-256 real y mocks
    // para JTI y auditoría. Se usa un TenantContext que se comporta como SuperAdmin para no
    // interferir con los filtros de tenant (el servicio ya usa IgnoreQueryFilters en recuperación).
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
                [ClaveNombre] = ClaveValor,
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
            Cifrado = cifrado,
        };
    }

    // Garantiza que exista el Comercio (tenant) con el Id indicado. En producción todo Usuario
    // pertenece a un Comercio real; sembrarlo mantiene coherente la navegación Usuario→Comercio que
    // usa GetPendingRecoveriesAsync (Include/ThenInclude) y refleja un estado de dominio válido.
    private static void AsegurarComercio(AppDbContext db, int comercioId)
    {
        if (db.Comercios.IgnoreQueryFilters().Any(c => c.Id == comercioId))
            return;

        db.Comercios.Add(new Comercio
        {
            Id = comercioId,
            Ruc = $"{comercioId:D13}",
            RazonSocial = $"Comercio {comercioId}",
            PlanId = 1,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    // Crea y persiste un usuario con el rol, comercio y contraseña conocida indicados.
    private static Usuario SembrarUsuario(AppDbContext db, string rol, int comercioId, string passwordPlano = "OriginalPass123@")
    {
        // Sembrar el Comercio asociado para que la navegación Usuario→Comercio resuelva a una entidad real.
        AsegurarComercio(db, comercioId);

        var usuario = new Usuario
        {
            ComercioId = comercioId,
            Nombre = $"Usuario {rol}",
            Email = $"{rol.ToLowerInvariant()}-{Guid.NewGuid():N}@test.com",
            PasswordHash = AuthService.HashPassword(passwordPlano),
            Rol = rol,
            Activo = true,
        };
        db.Usuarios.Add(usuario);
        db.SaveChanges();
        return usuario;
    }

    // Crea y persiste una SolicitudRecuperacion Pendiente reciente para el solicitante indicado.
    private static SolicitudRecuperacion SembrarSolicitudPendiente(AppDbContext db, Usuario solicitante)
    {
        var solicitud = new SolicitudRecuperacion
        {
            UsuarioId = solicitante.Id,
            ComercioId = solicitante.ComercioId,
            Estado = "Pendiente",
            FechaSolicitud = DateTime.UtcNow, // reciente: no vencida
        };
        db.SolicitudesRecuperacion.Add(solicitud);
        db.SaveChanges();
        return solicitud;
    }

    // ── Property 1 ──

    /// <summary>
    /// Feature: recuperacion-password-jerarquica, Property 1: Aprobación autorizada por jerarquía
    /// estricta y sin auto-aprobación.
    ///
    /// Para todo par (aprobador, solicitante) del mismo comercio, la aprobación tiene éxito si y
    /// solo si rank(rolAprobador) &gt; rank(rolSolicitante), el aprobador no es el propio solicitante
    /// y, en contexto POS, el aprobador es Dueño o Gerente. En cualquier otro caso se rechaza con
    /// Forbidden (403) sin modificar la solicitud.
    ///
    /// Este test cubre el contexto POS (mismo comercio): el aprobador se genera del conjunto
    /// {Dueño, Gerente} y el solicitante de todos los roles del dominio. El oráculo autoriza sii
    /// rank(aprobador) &gt; rank(solicitante) y no es auto-aprobación.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.4, 3.5, 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Aprobacion_ExitoSiiJerarquiaEstrictaYNoAutoAprobacion()
    {
        var gen =
            from aprobadorRol in Gen.Elements(RolesPos)
            from solicitanteRol in Gen.Elements(RolesConRango.Select(r => r.Rol).ToArray())
            // Distinguimos si el aprobador se aprueba a sí mismo: cuando los roles coinciden,
            // reutilizamos el mismo usuario como solicitante para probar la no auto-aprobación.
            from esAutoAprobacion in Gen.Elements(new[] { true, false })
            select (aprobadorRol, solicitanteRol, esAutoAprobacion);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (aprobadorRol, solicitanteRol, esAutoAprobacion) = tuple;

            using var ctx = CrearContexto();
            const int comercioId = 100;

            var aprobador = SembrarUsuario(ctx.Db, aprobadorRol, comercioId);

            // En el escenario de auto-aprobación, el solicitante ES el aprobador (mismo id y rol).
            Usuario solicitante;
            if (esAutoAprobacion)
            {
                solicitante = aprobador;
            }
            else
            {
                solicitante = SembrarUsuario(ctx.Db, solicitanteRol, comercioId);
            }

            var solicitud = SembrarSolicitudPendiente(ctx.Db, solicitante);
            var hashOriginal = solicitante.PasswordHash;

            var resultado = ctx.Servicio
                .ApprovePasswordRecoveryAsync(solicitud.Id, aprobador.Id)
                .GetAwaiter().GetResult();

            // Oráculo de autorización:
            // - Sin auto-aprobación.
            // - Jerarquía estricta rank(aprobador) > rank(solicitante).
            // (El aprobador ya es Dueño/Gerente por construcción → contexto POS satisfecho.)
            var rolSolicitanteEfectivo = esAutoAprobacion ? aprobadorRol : solicitanteRol;
            var esperadoAutorizado = !esAutoAprobacion
                && RoleHierarchy.CanApprove(aprobadorRol, rolSolicitanteEfectivo);

            // Recargar la solicitud para comprobar si fue modificada.
            var solicitudDespues = ctx.Db.SolicitudesRecuperacion
                .IgnoreQueryFilters()
                .First(s => s.Id == solicitud.Id);

            bool ok;
            if (esperadoAutorizado)
            {
                // Éxito: resultado ok y solicitud transicionada a Aprobada.
                ok = resultado.Success
                     && resultado.ErrorCode == RecoveryErrorCode.None
                     && solicitudDespues.Estado == "Aprobada";
            }
            else
            {
                // Rechazo por Forbidden (403), solicitud intacta (sigue Pendiente y sin hash rotado).
                var solicitanteDespues = ctx.Db.Usuarios
                    .IgnoreQueryFilters()
                    .First(u => u.Id == solicitante.Id);

                ok = !resultado.Success
                     && resultado.ErrorCode == RecoveryErrorCode.Forbidden
                     && solicitudDespues.Estado == "Pendiente"
                     && solicitanteDespues.PasswordHash == hashOriginal;
            }

            return ok.Label(
                $"aprobador={aprobadorRol}, solicitante={rolSolicitanteEfectivo}, auto={esAutoAprobacion}, " +
                $"esperadoAutorizado={esperadoAutorizado}, success={resultado.Success}, " +
                $"errorCode={resultado.ErrorCode}, estado={solicitudDespues.Estado}");
        });
    }

    // ── Property 2 ──

    /// <summary>
    /// Feature: recuperacion-password-jerarquica, Property 2: Aislamiento multi-tenant del alcance
    /// de aprobación.
    ///
    /// Para todo aprobador de contexto POS y toda solicitud, la solicitud es aprobable solo si
    /// pertenece al mismo ComercioId del aprobador; y para todo SuperAdmin, son aprobables
    /// exactamente las solicitudes de usuarios con rol Dueño de cualquier comercio (nunca las de
    /// roles no-Dueño por ese canal).
    ///
    /// Verificación combinada: se prueba tanto la aprobación (efecto) como el alcance visible de
    /// GetPendingRecoveriesAsync (Req 5.3) para el aislamiento por comercio.
    ///
    /// **Validates: Requirements 3.3, 3.7, 3.8, 5.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Aislamiento_AprobadorSoloAlcanzaSuComercioYSuperAdminSoloDuenos()
    {
        var gen =
            // Aprobador POS (Dueño/Gerente) o SuperAdmin cross-tenant.
            from aprobadorEsSuperAdmin in Gen.Elements(new[] { true, false })
            from aprobadorRolPos in Gen.Elements(RolesPos)
            // Solicitante: rol cualquiera y comercio igual o distinto del aprobador.
            from solicitanteRol in Gen.Elements(RolesConRango.Select(r => r.Rol).ToArray())
            from mismoComercio in Gen.Elements(new[] { true, false })
            select (aprobadorEsSuperAdmin, aprobadorRolPos, solicitanteRol, mismoComercio);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (aprobadorEsSuperAdmin, aprobadorRolPos, solicitanteRol, mismoComercio) = tuple;

            using var ctx = CrearContexto();
            const int comercioAprobador = 10;
            const int comercioOtro = 20;

            var aprobadorRol = aprobadorEsSuperAdmin ? "SuperAdmin" : aprobadorRolPos;
            // El SuperAdmin es cross-tenant; su comercio propio no restringe el alcance.
            var comercioDelAprobador = aprobadorEsSuperAdmin ? 999 : comercioAprobador;
            var aprobador = SembrarUsuario(ctx.Db, aprobadorRol, comercioDelAprobador);

            var comercioSolicitante = mismoComercio ? comercioAprobador : comercioOtro;

            // Oráculo del alcance (mismo para aprobación y listado, según las reglas de comercio/rol):
            // - SuperAdmin: solo solicitudes de usuarios con rol Dueño de cualquier comercio (Req 3.8).
            // - POS (Dueño/Gerente): mismo ComercioId (Req 3.7) y jerarquía estricta (Req 3.3).
            bool esperadoAutorizado;
            if (aprobadorEsSuperAdmin)
            {
                esperadoAutorizado = solicitanteRol == "Dueño";
            }
            else
            {
                esperadoAutorizado = mismoComercio
                    && RoleHierarchy.CanApprove(aprobadorRol, solicitanteRol);
            }

            // ── (a) Alcance de aprobación ──
            // Se ejercita sobre su PROPIA solicitud Pendiente: aprobar transiciona el estado, por eso
            // este efecto se mide de forma aislada y no se reutiliza para el listado.
            var solicitanteAprobacion = SembrarUsuario(ctx.Db, solicitanteRol, comercioSolicitante);
            var solicitudAprobacion = SembrarSolicitudPendiente(ctx.Db, solicitanteAprobacion);

            var resultado = ctx.Servicio
                .ApprovePasswordRecoveryAsync(solicitudAprobacion.Id, aprobador.Id)
                .GetAwaiter().GetResult();

            var aprobacionCoincide = esperadoAutorizado
                ? resultado.Success
                : (!resultado.Success && resultado.ErrorCode == RecoveryErrorCode.Forbidden);

            // ── (b) Aislamiento del listado (Req 5.3) ──
            // Se evalúa sobre una solicitud DISTINTA que permanece Pendiente (no aprobada), de modo que
            // la visibilidad refleja únicamente las reglas de comercio/rol y no el efecto de una aprobación
            // previa sobre el mismo registro. La solicitud aparece en la bandeja del aprobador sii está
            // dentro de su alcance.
            var solicitanteListado = SembrarUsuario(ctx.Db, solicitanteRol, comercioSolicitante);
            var solicitudListado = SembrarSolicitudPendiente(ctx.Db, solicitanteListado);

            var pendientes = ctx.Servicio
                .GetPendingRecoveriesAsync(aprobador.Id)
                .GetAwaiter().GetResult();
            var visible = pendientes.Any(p => p.RequestId == solicitudListado.Id);

            // La visibilidad esperada usa exactamente las mismas reglas de alcance que la aprobación.
            var visibilidadEsperada = esperadoAutorizado;

            var listadoCoincide = visible == visibilidadEsperada;

            return (aprobacionCoincide && listadoCoincide).Label(
                $"aprobador={aprobadorRol}, solicitante={solicitanteRol}, mismoComercio={mismoComercio}, " +
                $"esperadoAutorizado={esperadoAutorizado}, success={resultado.Success}, err={resultado.ErrorCode}, " +
                $"visible={visible}, visibilidadEsperada={visibilidadEsperada}");
        });
    }

    // ── Property 4 ──

    /// <summary>
    /// Feature: recuperacion-password-jerarquica, Property 4: Una aprobación válida deja el estado
    /// consistente.
    ///
    /// Para toda aprobación autorizada, tras ejecutarla: BCrypt.Verify(temporalGenerada,
    /// solicitante.PasswordHash) es verdadero (BCrypt WF ≥ 12), solicitante.DebeCambiarPassword es
    /// verdadero, la solicitud queda en Estado "Aprobada" con AprobadoPor, FechaResolucion y
    /// FechaExpiracion = FechaResolucion + 24h establecidos, y PasswordTemporalCifrada no es nula
    /// (y descifra a la temporal devuelta).
    ///
    /// El generador produce solo pares AUTORIZADOS (rank(aprobador) &gt; rank(solicitante), aprobador
    /// Dueño/Gerente del mismo comercio), de modo que toda ejecución debe cumplir la postcondición.
    ///
    /// **Validates: Requirements 4.3, 4.4, 4.5, 4.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AprobacionValida_DejaEstadoConsistente()
    {
        // Solo pares autorizados en contexto POS: aprobador Dueño/Gerente y solicitante de rango
        // estrictamente inferior dentro del mismo comercio.
        var gen =
            from aprobadorRol in Gen.Elements(RolesPos)
            from solicitanteRol in Gen.Elements(RolesConRango.Select(r => r.Rol).ToArray())
            where RoleHierarchy.CanApprove(aprobadorRol, solicitanteRol)
            select (aprobadorRol, solicitanteRol);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (aprobadorRol, solicitanteRol) = tuple;

            using var ctx = CrearContexto();
            const int comercioId = 42;

            var aprobador = SembrarUsuario(ctx.Db, aprobadorRol, comercioId);
            var solicitante = SembrarUsuario(ctx.Db, solicitanteRol, comercioId);
            var solicitud = SembrarSolicitudPendiente(ctx.Db, solicitante);

            var antes = DateTime.UtcNow;

            var resultado = ctx.Servicio
                .ApprovePasswordRecoveryAsync(solicitud.Id, aprobador.Id)
                .GetAwaiter().GetResult();

            var despues = DateTime.UtcNow;

            // La aprobación debe ser exitosa y devolver la temporal en texto plano.
            if (!resultado.Success || string.IsNullOrEmpty(resultado.TempPassword))
            {
                return false.Label($"Aprobación no exitosa: success={resultado.Success}, err={resultado.ErrorCode}");
            }

            var tempPlano = resultado.TempPassword!;

            var solicitanteDespues = ctx.Db.Usuarios
                .IgnoreQueryFilters()
                .First(u => u.Id == solicitante.Id);
            var solicitudDespues = ctx.Db.SolicitudesRecuperacion
                .IgnoreQueryFilters()
                .First(s => s.Id == solicitud.Id);

            // (Req 4.3) El hash rota y BCrypt.Verify de la temporal es verdadero (WF ≥ 12).
            var hashVerifica = BCrypt.Net.BCrypt.Verify(tempPlano, solicitanteDespues.PasswordHash);
            var workFactorOk = ExtraerWorkFactor(solicitanteDespues.PasswordHash) >= 12;

            // (Req 4.4) La bandera de cambio obligatorio queda activa.
            var banderaOk = solicitanteDespues.DebeCambiarPassword;

            // (Req 4.6) Estado Aprobada con AprobadoPor y FechaResolucion; y FechaExpiracion = resolución + 24h.
            var estadoOk = solicitudDespues.Estado == "Aprobada"
                && solicitudDespues.AprobadoPor == aprobador.Id
                && solicitudDespues.FechaResolucion.HasValue
                && solicitudDespues.FechaResolucion.Value >= antes
                && solicitudDespues.FechaResolucion.Value <= despues;

            var expiracionOk = solicitudDespues.FechaExpiracion.HasValue
                && solicitudDespues.FechaResolucion.HasValue
                // Tolerancia de 1s por precisión de DateTime en el cálculo del servicio.
                && Math.Abs((solicitudDespues.FechaExpiracion.Value
                             - solicitudDespues.FechaResolucion.Value).TotalHours - 24) < (1.0 / 3600.0)
                && (resultado.FechaExpiracion == solicitudDespues.FechaExpiracion);

            // (Req 4.5) PasswordTemporalCifrada no es nula y descifra a la temporal devuelta.
            var cifradoOk = solicitudDespues.PasswordTemporalCifrada != null
                && ctx.Cifrado.Decrypt(solicitudDespues.PasswordTemporalCifrada!) == tempPlano;

            var ok = hashVerifica && workFactorOk && banderaOk && estadoOk && expiracionOk && cifradoOk;

            return ok.Label(
                $"aprobador={aprobadorRol}, solicitante={solicitanteRol}, hashVerifica={hashVerifica}, " +
                $"wfOk={workFactorOk}, bandera={banderaOk}, estadoOk={estadoOk}, expiracionOk={expiracionOk}, " +
                $"cifradoOk={cifradoOk}");
        });
    }

    /// <summary>
    /// Extrae el work factor (coste) de un hash BCrypt con formato "$2a$NN$...".
    /// Devuelve -1 si el formato no es reconocible.
    /// </summary>
    private static int ExtraerWorkFactor(string bcryptHash)
    {
        // Formato: $<version>$<costo>$<salt+hash>. El costo son los dos dígitos tras el segundo '$'.
        var partes = bcryptHash.Split('$');
        if (partes.Length < 4)
            return -1;

        return int.TryParse(partes[2], out var costo) ? costo : -1;
    }
}
