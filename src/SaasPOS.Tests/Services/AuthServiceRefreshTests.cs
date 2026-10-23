using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;
using Xunit;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Feature: sesion-inactividad-y-expiracion-por-rol, tarea 7.1
/// ("Unit tests (xUnit/Moq) de GenerateJwt y RefreshTokenAsync").
///
/// Ejercita el backend de sesión por rol ya implementado en <see cref="AuthService"/> (tareas 2 y 3):
///   - Resolución de la vida del access token por rol (invariante vida == ventana, Req 3.4, 3.6):
///     la vida emitida (exp - iat) iguala InactivityMinutesByRole[rol] (fallback InactivityMinutesDefault),
///     y un valor divergente en AccessTokenMinutesByRole NO altera la vida.
///   - Resolución del tope absoluto por rol en RefreshTokenAsync (incluida la "ñ" de "Dueño" y el
///     fallback AbsoluteMaxMinutesDefault para un rol no listado) (Req 4.3, 4.4).
///   - login_at fijado en el login (= iat) (Req 4.1) y propagado SIN cambios en la renovación, con
///     nuevo jti y nuevo iat (Req 4.2, 4.6).
///   - Tope superado → RefreshResult.Fail (sin token); dentro del tope → RefreshResult.Ok preservando
///     los claims funcionales (sub, comercio_id, role y, cuando aplican, sucursal_id/must_change_password)
///     (Req 1.6, 4.3, 4.4).
///   - Refresh de un token sin sucursal_id ni must_change_password: no lanza y el renovado tampoco los
///     incluye (preserva su ausencia).
///
/// GenerateJwt permanece private: se ejercita indirectamente vía LoginAsync (login) y vía
/// RefreshTokenAsync (renovación). El JWT se decodifica con JwtSecurityTokenHandler leyendo los
/// claims crudos (MapInboundClaims = false en producción), sin validar firma/vida.
/// </summary>
public class AuthServiceRefreshTests
{
    // Secreto de cifrado AES-256 en memoria (no interviene en estos flujos, pero el servicio lo requiere).
    private const string EncryptionKeyName = "PASSWORD_RECOVERY_ENCRYPTION_KEY";
    private const string EncryptionKeyValue = "clave-de-prueba-unit-tests-refresh-32bytes++++++++++++++";

    // Contraseña conocida que cumple la Politica_Password (mayúscula, dígito, especial permitido).
    private const string KnownValidPassword = "Abc-123";

    // Rol deliberadamente ausente de los diccionarios por rol para ejercitar los fallbacks globales.
    private const string RolNoListado = "RolInexistente";

    // Vidas/topes por rol usados en las pruebas (minutos). Las claves usan el nombre EXACTO de
    // Usuario.Rol, incluida la "ñ" de "Dueño".
    private static readonly Dictionary<string, int> InactividadPorRol = new()
    {
        ["SuperAdmin"] = 15,
        ["Dueño"] = 30,
        ["Gerente"] = 30,
        ["Supervisor"] = 60,
        ["Bodeguero"] = 60,
        ["Cajero"] = 60,
    };

    private static readonly Dictionary<string, int> TopeAbsolutoPorRol = new()
    {
        ["SuperAdmin"] = 240,
        ["Dueño"] = 480,
        ["Gerente"] = 480,
        ["Supervisor"] = 720,
        ["Bodeguero"] = 720,
        ["Cajero"] = 720,
    };

    // JwtSettings de prueba. AccessTokenMinutesByRole se deja DELIBERADAMENTE divergente de
    // InactivityMinutesByRole (valores absurdos de 999) para verificar que la vida emitida se toma
    // SIEMPRE de InactivityMinutesByRole y nunca de AccessTokenMinutesByRole (invariante vida == ventana).
    private static JwtSettings CrearJwtSettings() => new()
    {
        SecretKey = "ThisIsATestSecretKeyThatIsLongEnoughForHmacSha256Algorithm!!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpirationMinutes = 999, // divergente a propósito: tampoco debe usarse para la vida del token.
        AccessTokenMinutesByRole = new Dictionary<string, int>
        {
            ["SuperAdmin"] = 999,
            ["Dueño"] = 999,
            ["Gerente"] = 999,
            ["Supervisor"] = 999,
            ["Bodeguero"] = 999,
            ["Cajero"] = 999,
        },
        InactivityMinutesByRole = new Dictionary<string, int>(InactividadPorRol),
        AbsoluteMaxMinutesByRole = new Dictionary<string, int>(TopeAbsolutoPorRol),
        InactivityMinutesDefault = 60,
        AbsoluteMaxMinutesDefault = 720,
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
    // El ITenantContext se configura como SuperAdmin para no interferir con los filtros de tenant
    // (login/refresh ya usan IgnoreQueryFilters donde corresponde).
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

        var servicio = new AuthService(
            db,
            Options.Create(CrearJwtSettings()),
            new Mock<IJtiBlocklist>().Object,
            cifrado,
            new Mock<IAuditService>().Object);

        return new ContextoPrueba { Db = db, Servicio = servicio };
    }

    // Crea y persiste un usuario activo con el rol, comercio y contraseña conocida indicados.
    private static Usuario SembrarUsuario(AppDbContext db, string rol, int comercioId, int? sucursalId, string emailPrefix)
    {
        var usuario = new Usuario
        {
            ComercioId = comercioId,
            SucursalId = sucursalId,
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

    private static JwtSecurityToken Decodificar(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token);

    // Lee un claim numérico (epoch Unix) del JWT como long.
    private static long LeerClaimLong(JwtSecurityToken jwt, string claimType)
    {
        var claim = Assert.Single(jwt.Claims, c => c.Type == claimType);
        return long.Parse(claim.Value);
    }

    // Lee un claim de texto del JWT.
    private static string LeerClaimString(JwtSecurityToken jwt, string claimType)
    {
        var claim = Assert.Single(jwt.Claims, c => c.Type == claimType);
        return claim.Value;
    }

    // Instante actual en epoch de segundos UTC (misma base que usa GenerateJwt para iat/login_at).
    private static long AhoraUnix() => new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();

    // ─────────────────────────────────────────────────────────────────────────────
    // Caso 1 — Resolución de la vida del token por rol (invariante vida == ventana).
    // La vida emitida en login (exp - iat) iguala InactivityMinutesByRole[rol] (en segundos),
    // o InactivityMinutesDefault para un rol no listado. AccessTokenMinutesByRole/ExpirationMinutes
    // (ambos a 999 a propósito) NO deben alterar la vida. **Validates: Requirements 3.4, 3.6**
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SuperAdmin", 15)]
    [InlineData("Dueño", 30)]
    [InlineData("Gerente", 30)]
    [InlineData("Supervisor", 60)]
    [InlineData("Bodeguero", 60)]
    [InlineData("Cajero", 60)]
    public async Task Login_VidaDelTokenIgualaVentanaDeInactividadDelRol(string rol, int ventanaMinutos)
    {
        using var ctx = CrearContexto();
        var usuario = SembrarUsuario(ctx.Db, rol, comercioId: 100, sucursalId: 7, emailPrefix: "vida");

        var resultado = await ctx.Servicio.LoginAsync(usuario.Email, KnownValidPassword);
        Assert.True(resultado.Success);

        var jwt = Decodificar(resultado.Token!);
        var iat = LeerClaimLong(jwt, JwtRegisteredClaimNames.Iat);
        // exp lo expone JwtSecurityToken como epoch de segundos UTC a través de ValidTo.
        var exp = new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds();

        // La vida (exp - iat) debe igualar la ventana del rol en segundos, tomada de
        // InactivityMinutesByRole y NO de AccessTokenMinutesByRole (999) ni ExpirationMinutes (999).
        Assert.Equal(ventanaMinutos * 60L, exp - iat);
    }

    [Fact]
    public async Task Login_RolNoListado_VidaUsaFallbackInactivityMinutesDefault()
    {
        using var ctx = CrearContexto();
        var usuario = SembrarUsuario(ctx.Db, RolNoListado, comercioId: 100, sucursalId: null, emailPrefix: "vida-fallback");

        var resultado = await ctx.Servicio.LoginAsync(usuario.Email, KnownValidPassword);
        Assert.True(resultado.Success);

        var jwt = Decodificar(resultado.Token!);
        var iat = LeerClaimLong(jwt, JwtRegisteredClaimNames.Iat);
        var exp = new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds();

        // Rol ausente de los diccionarios → la vida cae al fallback InactivityMinutesDefault (60 min).
        Assert.Equal(60 * 60L, exp - iat);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Caso 2 — Resolución del tope absoluto por rol en RefreshTokenAsync.
    // Para cada rol, un login_at justo DENTRO del tope renueva (Ok) y uno justo FUERA falla (Fail).
    // Incluye la "ñ" de "Dueño" y el fallback AbsoluteMaxMinutesDefault para un rol no listado.
    // **Validates: Requirements 4.3, 4.4**
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SuperAdmin", 240)]
    [InlineData("Dueño", 480)]
    [InlineData("Gerente", 480)]
    [InlineData("Supervisor", 720)]
    [InlineData("Bodeguero", 720)]
    [InlineData("Cajero", 720)]
    public async Task Refresh_TopeAbsolutoPorRol_DentroRenueva_FueraFalla(string rol, int topeMinutos)
    {
        using var ctx = CrearContexto();

        // login_at justo DENTRO del tope (tope - 1 min) → debe renovar (Ok).
        var loginAtDentro = AhoraUnix() - (topeMinutos - 1) * 60L;
        var dentro = await ctx.Servicio.RefreshTokenAsync(
            userId: 1, comercioId: 10, role: rol, sucursalId: 3, mustChangePassword: false, loginAtUnix: loginAtDentro);
        Assert.True(dentro.Success);
        Assert.False(string.IsNullOrWhiteSpace(dentro.Token));

        // login_at justo FUERA del tope (tope + 1 min) → NO debe renovar (Fail, sin token).
        var loginAtFuera = AhoraUnix() - (topeMinutos + 1) * 60L;
        var fuera = await ctx.Servicio.RefreshTokenAsync(
            userId: 1, comercioId: 10, role: rol, sucursalId: 3, mustChangePassword: false, loginAtUnix: loginAtFuera);
        Assert.False(fuera.Success);
        Assert.Null(fuera.Token);
    }

    [Fact]
    public async Task Refresh_RolNoListado_UsaFallbackAbsoluteMaxMinutesDefault()
    {
        using var ctx = CrearContexto();
        // Fallback AbsoluteMaxMinutesDefault = 720 min.

        var loginAtDentro = AhoraUnix() - (720 - 1) * 60L;
        var dentro = await ctx.Servicio.RefreshTokenAsync(
            userId: 1, comercioId: 10, role: RolNoListado, sucursalId: null, mustChangePassword: false, loginAtUnix: loginAtDentro);
        Assert.True(dentro.Success);

        var loginAtFuera = AhoraUnix() - (720 + 1) * 60L;
        var fuera = await ctx.Servicio.RefreshTokenAsync(
            userId: 1, comercioId: 10, role: RolNoListado, sucursalId: null, mustChangePassword: false, loginAtUnix: loginAtFuera);
        Assert.False(fuera.Success);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Caso 3 — login_at en login (= iat). **Validates: Requirements 4.1**
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_FijaLoginAtIgualAIat()
    {
        using var ctx = CrearContexto();
        var usuario = SembrarUsuario(ctx.Db, "Cajero", comercioId: 100, sucursalId: 5, emailPrefix: "loginat");

        var resultado = await ctx.Servicio.LoginAsync(usuario.Email, KnownValidPassword);
        Assert.True(resultado.Success);

        var jwt = Decodificar(resultado.Token!);
        var iat = LeerClaimLong(jwt, JwtRegisteredClaimNames.Iat);
        var loginAt = LeerClaimLong(jwt, "login_at");

        Assert.Equal(iat, loginAt);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Caso 4 — login_at propagado SIN cambios en la renovación, con nuevo jti y nuevo iat.
    // Se parte del token de login y se renueva con su propio login_at. El token renovado debe
    // conservar el mismo login_at pero traer jti e iat frescos. **Validates: Requirements 4.2, 4.6**
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_PropagaLoginAt_ConNuevoJtiEIat()
    {
        using var ctx = CrearContexto();
        var usuario = SembrarUsuario(ctx.Db, "Gerente", comercioId: 42, sucursalId: 9, emailPrefix: "propaga");

        var login = await ctx.Servicio.LoginAsync(usuario.Email, KnownValidPassword);
        Assert.True(login.Success);

        var jwtLogin = Decodificar(login.Token!);
        var loginAtOriginal = LeerClaimLong(jwtLogin, "login_at");
        var iatLogin = LeerClaimLong(jwtLogin, JwtRegisteredClaimNames.Iat);
        var jtiLogin = LeerClaimString(jwtLogin, JwtRegisteredClaimNames.Jti);

        // Pequeña pausa para forzar un iat distinto (epoch en segundos) en la renovación.
        await Task.Delay(1100);

        var refresh = await ctx.Servicio.RefreshTokenAsync(
            userId: usuario.Id,
            comercioId: usuario.ComercioId,
            role: usuario.Rol,
            sucursalId: usuario.SucursalId,
            mustChangePassword: false,
            loginAtUnix: loginAtOriginal);

        Assert.True(refresh.Success);

        var jwtRefresh = Decodificar(refresh.Token!);
        var loginAtRenovado = LeerClaimLong(jwtRefresh, "login_at");
        var iatRenovado = LeerClaimLong(jwtRefresh, JwtRegisteredClaimNames.Iat);
        var jtiRenovado = LeerClaimString(jwtRefresh, JwtRegisteredClaimNames.Jti);

        // login_at propagado idéntico; jti e iat frescos (distintos a los del login original).
        Assert.Equal(loginAtOriginal, loginAtRenovado);
        Assert.NotEqual(jtiLogin, jtiRenovado);
        Assert.True(iatRenovado > iatLogin, "El iat renovado debe ser posterior al del login original.");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Caso 5 — Tope superado → RefreshResult.Fail sin emitir token.
    // **Validates: Requirements 4.3, 4.4**
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_TopeSuperado_DevuelveFailSinToken()
    {
        using var ctx = CrearContexto();
        // Cajero: tope 720 min. login_at muy antiguo (1000 min atrás) supera el tope.
        var loginAtMuyAntiguo = AhoraUnix() - 1000 * 60L;

        var resultado = await ctx.Servicio.RefreshTokenAsync(
            userId: 1, comercioId: 10, role: "Cajero", sucursalId: 2, mustChangePassword: false, loginAtUnix: loginAtMuyAntiguo);

        Assert.False(resultado.Success);
        Assert.Null(resultado.Token);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Caso 6 — Dentro del tope → RefreshResult.Ok preservando los claims funcionales.
    // sub, comercio_id, role, y sucursal_id/must_change_password cuando aplican.
    // **Validates: Requirements 1.6, 4.4**
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_DentroDelTope_PreservaClaimsFuncionales()
    {
        using var ctx = CrearContexto();
        var loginAtReciente = AhoraUnix() - 5 * 60L; // 5 min atrás (holgadamente dentro del tope).

        var resultado = await ctx.Servicio.RefreshTokenAsync(
            userId: 777,
            comercioId: 55,
            role: "Dueño",
            sucursalId: 13,
            mustChangePassword: true,
            loginAtUnix: loginAtReciente);

        Assert.True(resultado.Success);

        var jwt = Decodificar(resultado.Token!);
        Assert.Equal("777", LeerClaimString(jwt, JwtRegisteredClaimNames.Sub));
        Assert.Equal("55", LeerClaimString(jwt, "comercio_id"));
        Assert.Equal("Dueño", LeerClaimString(jwt, "role"));
        Assert.Equal("13", LeerClaimString(jwt, "sucursal_id"));
        Assert.Equal("true", LeerClaimString(jwt, "must_change_password"));
        // login_at propagado idéntico al presentado.
        Assert.Equal(loginAtReciente, LeerClaimLong(jwt, "login_at"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Caso 7 — Refresh de un token sin sucursal_id ni must_change_password:
    // no lanza y el token renovado TAMPOCO incluye esos claims (preserva su ausencia).
    // **Validates: Requirements 1.6, 4.4**
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_SinSucursalNiMustChange_NoIncluyeEsosClaims()
    {
        using var ctx = CrearContexto();
        var loginAtReciente = AhoraUnix() - 5 * 60L;

        // SuperAdmin sin sucursal y sin obligación de cambio de contraseña.
        var resultado = await ctx.Servicio.RefreshTokenAsync(
            userId: 1,
            comercioId: 1,
            role: "SuperAdmin",
            sucursalId: null,
            mustChangePassword: false,
            loginAtUnix: loginAtReciente);

        Assert.True(resultado.Success);

        var jwt = Decodificar(resultado.Token!);

        // Los claims obligatorios siguen presentes.
        Assert.Equal("1", LeerClaimString(jwt, JwtRegisteredClaimNames.Sub));
        Assert.Equal("1", LeerClaimString(jwt, "comercio_id"));
        Assert.Equal("SuperAdmin", LeerClaimString(jwt, "role"));

        // Los claims opcionales NO deben emitirse cuando no aplican (preserva su ausencia).
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "sucursal_id");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "must_change_password");
    }
}
