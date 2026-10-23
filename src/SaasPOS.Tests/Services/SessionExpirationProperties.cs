using System.IdentityModel.Tokens.Jwt;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Feature: sesion-inactividad-y-expiracion-por-rol, tarea 7.3
/// ("PBT (FsCheck ≥100) de las invariantes de sesión por rol").
///
/// Pruebas basadas en propiedades (FsCheck.Xunit, ≥100 iteraciones por propiedad) de las dos
/// invariantes normativas del backend de sesión por rol implementado en <see cref="AuthService"/>
/// (tareas 2 y 3):
///
///   Property 1 — vida == ventana para TODO rol (Req 3.4, 3.6):
///     Para cualquier rol (de los diccionarios o uno arbitrario no listado) y cualquier valor
///     divergente en AccessTokenMinutesByRole, la vida del access token emitido por GenerateJwt
///     (exp - iat) iguala la ventana resuelta: InactivityMinutesByRole[rol] si está listado, o
///     InactivityMinutesDefault si no lo está. AccessTokenMinutesByRole y ExpirationMinutes NO
///     alteran la vida emitida.
///
///   Property 2 — tope absoluto (Req 4.3, 4.4):
///     Para cualquier login_at y rol, RefreshTokenAsync devuelve Fail si y solo si el tiempo
///     transcurrido (now - login_at) supera el tope absoluto resuelto del rol
///     (AbsoluteMaxMinutesByRole[rol] o AbsoluteMaxMinutesDefault), y Ok en caso contrario. El
///     cálculo es en UTC.
///
/// GenerateJwt permanece private: se ejercita indirectamente vía LoginAsync (Property 1) y vía
/// RefreshTokenAsync (Property 2). El JWT se decodifica con JwtSecurityTokenHandler leyendo los
/// claims crudos (MapInboundClaims = false en producción), sin validar firma/vida.
///
/// Aislamiento entre iteraciones: FsCheck reejecuta el cuerpo muchas veces, así que CADA iteración
/// construye un AuthService FRESCO sobre una base EF Core InMemory única (por GUID). Así no hay
/// estado residual ni colisiones de email/usuario entre ejecuciones.
/// </summary>
public class SessionExpirationProperties
{
    // Secreto de cifrado AES-256 en memoria (no interviene en estos flujos, pero el servicio lo requiere).
    private const string EncryptionKeyName = "PASSWORD_RECOVERY_ENCRYPTION_KEY";
    private const string EncryptionKeyValue = "clave-de-prueba-pbt-sesion-por-rol-32bytes++++++++++++++++";

    // Contraseña conocida que cumple la Politica_Password (mayúscula, dígito, especial permitido).
    private const string KnownValidPassword = "Abc-123";

    // Ventana de inactividad (= vida del access token, por el invariante vida == ventana) por rol,
    // en minutos. Claves con el nombre EXACTO de Usuario.Rol, incluida la "ñ" de "Dueño".
    private static readonly Dictionary<string, int> InactividadPorRol = new()
    {
        ["SuperAdmin"] = 15,
        ["Dueño"] = 30,
        ["Gerente"] = 30,
        ["Supervisor"] = 60,
        ["Bodeguero"] = 60,
        ["Cajero"] = 60,
    };

    // Tope absoluto (minutos) por rol. Mismas claves que InactividadPorRol.
    private static readonly Dictionary<string, int> TopeAbsolutoPorRol = new()
    {
        ["SuperAdmin"] = 240,
        ["Dueño"] = 480,
        ["Gerente"] = 480,
        ["Supervisor"] = 720,
        ["Bodeguero"] = 720,
        ["Cajero"] = 720,
    };

    // Roles conocidos de los diccionarios (los que debe ejercitar la propiedad además del no listado).
    private static readonly string[] RolesListados =
    {
        "SuperAdmin", "Dueño", "Gerente", "Supervisor", "Bodeguero", "Cajero",
    };

    // Fallbacks globales (coherentes con appsettings y con el diseño).
    private const int InactivityMinutesDefault = 60;
    private const int AbsoluteMaxMinutesDefault = 720;

    // JwtSettings de prueba. AccessTokenMinutesByRole se fija DELIBERADAMENTE divergente de
    // InactivityMinutesByRole (valor "divergente" parametrizable por iteración) para probar que la
    // vida emitida se toma SIEMPRE de InactivityMinutesByRole y nunca de AccessTokenMinutesByRole
    // (invariante vida == ventana). ExpirationMinutes también se deja absurdo (999) por la misma razón.
    private static JwtSettings CrearJwtSettings(int accessTokenDivergente) => new()
    {
        SecretKey = "ThisIsATestSecretKeyThatIsLongEnoughForHmacSha256Algorithm!!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpirationMinutes = 999, // divergente a propósito: tampoco debe usarse para la vida del token.
        AccessTokenMinutesByRole = new Dictionary<string, int>
        {
            // Todas las claves conocidas con un valor divergente (!= ventana) para cada iteración.
            ["SuperAdmin"] = accessTokenDivergente,
            ["Dueño"] = accessTokenDivergente,
            ["Gerente"] = accessTokenDivergente,
            ["Supervisor"] = accessTokenDivergente,
            ["Bodeguero"] = accessTokenDivergente,
            ["Cajero"] = accessTokenDivergente,
        },
        InactivityMinutesByRole = new Dictionary<string, int>(InactividadPorRol),
        AbsoluteMaxMinutesByRole = new Dictionary<string, int>(TopeAbsolutoPorRol),
        InactivityMinutesDefault = InactivityMinutesDefault,
        AbsoluteMaxMinutesDefault = AbsoluteMaxMinutesDefault,
    };

    // Contexto de prueba autocontenido: el AppDbContext InMemory y el AuthService bajo prueba.
    private sealed class ContextoPrueba : IDisposable
    {
        public required AppDbContext Db { get; init; }
        public required AuthService Servicio { get; init; }

        public void Dispose() => Db.Dispose();
    }

    // Construye un AuthService real sobre EF Core InMemory (aislado por Guid) con cifrado AES-256 real
    // y mocks de las dependencias que no intervienen en estos flujos (JTI blocklist y auditoría).
    // Mismo patrón que AuthServiceRefreshTests (tarea 7.1), replicado para que cada iteración de
    // FsCheck parta de un estado limpio.
    private static ContextoPrueba CrearContexto(int accessTokenDivergente)
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

        var servicio = new AuthService(
            db,
            Options.Create(CrearJwtSettings(accessTokenDivergente)),
            new Mock<IJtiBlocklist>().Object,
            cifrado,
            new Mock<IAuditService>().Object);

        return new ContextoPrueba { Db = db, Servicio = servicio };
    }

    // Crea y persiste un usuario activo con el rol y contraseña conocida indicados.
    private static Usuario SembrarUsuario(AppDbContext db, string rol)
    {
        var usuario = new Usuario
        {
            ComercioId = 100,
            SucursalId = 7,
            Nombre = "Usuario PBT",
            Email = $"pbt-{Guid.NewGuid():N}@test.com",
            PasswordHash = AuthService.HashPassword(KnownValidPassword),
            Rol = rol,
            Activo = true,
        };
        db.Usuarios.Add(usuario);
        db.SaveChanges();
        return usuario;
    }

    private static JwtSecurityToken Decodificar(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token);

    private static long LeerClaimLong(JwtSecurityToken jwt, string claimType)
    {
        var claim = jwt.Claims.Single(c => c.Type == claimType);
        return long.Parse(claim.Value);
    }

    // Instante actual en epoch de segundos UTC (misma base que usa GenerateJwt para iat/login_at).
    private static long AhoraUnix() => new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();

    // Ventana esperada (minutos) para un rol: InactivityMinutesByRole[rol] o el fallback por defecto.
    private static int VentanaEsperada(string rol) =>
        InactividadPorRol.TryGetValue(rol, out var m) ? m : InactivityMinutesDefault;

    // Tope absoluto esperado (minutos) para un rol: AbsoluteMaxMinutesByRole[rol] o el fallback.
    private static int TopeEsperado(string rol) =>
        TopeAbsolutoPorRol.TryGetValue(rol, out var m) ? m : AbsoluteMaxMinutesDefault;

    // Selecciona el rol a partir de un entero arbitrario de FsCheck: con probabilidad alta elige
    // uno de los roles LISTADOS (deterministamente, por el módulo) y, para un valor concreto,
    // devuelve un rol NO listado, de modo que la propiedad ejercite ambos caminos (listado + fallback).
    private static string RolDesde(int seleccion)
    {
        // Normaliza a [0, RolesListados.Length] (un valor extra reservado para el rol no listado).
        var indice = Math.Abs(seleccion % (RolesListados.Length + 1));
        return indice < RolesListados.Length ? RolesListados[indice] : "RolInexistentePBT";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Feature: sesion-inactividad-y-expiracion-por-rol, Property 1:
    // vida == ventana para TODO rol. La vida del access token emitido en login (exp - iat) iguala
    // la ventana resuelta (InactivityMinutesByRole[rol] o InactivityMinutesDefault), y un valor
    // DIVERGENTE y arbitrario en AccessTokenMinutesByRole NO altera la vida emitida.
    // **Validates: Requirements 3.4, 3.6**
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Para cualquier rol (listado o no listado) y cualquier valor divergente de
    /// AccessTokenMinutesByRole, la vida del token de login iguala la ventana de inactividad del rol
    /// (fallback InactivityMinutesDefault para un rol no listado). Se exige que el divergente sea
    /// distinto de la ventana esperada, para que la igualdad vida == ventana demuestre que
    /// AccessTokenMinutesByRole se ignora.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool Property1_VidaIgualaVentanaParaTodoRol(int seleccionRol, PositiveInt divergenteGenerado)
    {
        var rol = RolDesde(seleccionRol);
        var ventanaMinutos = VentanaEsperada(rol);

        // Valor divergente y arbitrario para AccessTokenMinutesByRole, garantizado != ventana
        // (si por azar coincidiera, se le suma la propia ventana para forzar la divergencia).
        var divergente = 1 + (divergenteGenerado.Get % 10000);
        if (divergente == ventanaMinutos)
            divergente += ventanaMinutos + 1;

        using var ctx = CrearContexto(divergente);
        var usuario = SembrarUsuario(ctx.Db, rol);

        var login = ctx.Servicio.LoginAsync(usuario.Email, KnownValidPassword).GetAwaiter().GetResult();
        if (!login.Success || string.IsNullOrWhiteSpace(login.Token))
            return false;

        var jwt = Decodificar(login.Token);
        var iat = LeerClaimLong(jwt, JwtRegisteredClaimNames.Iat);
        // exp lo expone JwtSecurityToken como epoch de segundos UTC a través de ValidTo.
        var exp = new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds();

        // La vida (exp - iat) debe igualar la ventana del rol en segundos, independientemente del
        // valor divergente de AccessTokenMinutesByRole y de ExpirationMinutes (999).
        return (exp - iat) == ventanaMinutos * 60L;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Feature: sesion-inactividad-y-expiracion-por-rol, Property 2:
    // Tope absoluto. Para cualquier login_at y rol, now - login_at > topeAbsoluto(role) ⇒ Fail;
    // en caso contrario ⇒ Ok. Cálculo en UTC.
    // **Validates: Requirements 4.3, 4.4**
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Para cualquier rol (listado o no listado) y cualquier antigüedad del login (en minutos),
    /// RefreshTokenAsync devuelve Fail si y solo si (now - login_at) supera el tope absoluto del rol.
    /// Se usa un desplazamiento mínimo de 1 minuto respecto al borde del tope para evitar la
    /// ambigüedad del instante exacto (el reloj avanza entre calcular login_at y medir en el servicio).
    /// </summary>
    [Property(MaxTest = 200)]
    public bool Property2_TopeAbsoluto_FueraFalla_DentroOk(int seleccionRol, PositiveInt desplazamientoGenerado, bool fuera)
    {
        var rol = RolDesde(seleccionRol);
        var topeMinutos = TopeEsperado(rol);

        // El valor divergente de AccessTokenMinutesByRole no afecta a esta propiedad; se fija a 999.
        using var ctx = CrearContexto(accessTokenDivergente: 999);

        // Desplazamiento de al menos 1 minuto (y acotado) respecto al borde del tope, para no caer
        // justo en el instante ambiguo. "fuera": login_at anterior al tope (supera) → Fail esperado.
        var deltaMinutos = 1 + (desplazamientoGenerado.Get % 600);

        var antiguedadMinutos = fuera
            ? topeMinutos + deltaMinutos   // supera el tope → debe fallar
            : Math.Max(0, topeMinutos - deltaMinutos); // dentro del tope → debe renovar

        var loginAtUnix = AhoraUnix() - antiguedadMinutos * 60L;

        var resultado = ctx.Servicio.RefreshTokenAsync(
            userId: 1,
            comercioId: 10,
            role: rol,
            sucursalId: 3,
            mustChangePassword: false,
            loginAtUnix: loginAtUnix).GetAwaiter().GetResult();

        if (fuera)
        {
            // Tope superado → Fail sin token.
            return !resultado.Success && resultado.Token is null;
        }

        // Dentro del tope → Ok con token no vacío.
        return resultado.Success && !string.IsNullOrWhiteSpace(resultado.Token);
    }
}
