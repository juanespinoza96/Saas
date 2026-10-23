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
/// Feature: sesion-inactividad-y-expiracion-por-rol, tarea 2.2 ("Fijar login_at en el login original").
/// Unit tests (xUnit) que confirman que el token emitido por <see cref="AuthService.LoginAsync"/>
/// incluye el claim <c>login_at</c> con un valor igual a <c>iat</c> (Req 4.1, 4.2).
///
/// El login original invoca <c>GenerateJwt(..., loginAtUnix: null)</c>, por lo que <c>GenerateJwt</c>
/// fija <c>login_at = iat</c> en el primer token de la sesión. Este mismo flujo atiende al SuperAdmin
/// (no hay emisión de token propia para ese rol), de modo que su primer token también cumple la
/// igualdad. Las renovaciones posteriores (fuera del alcance de esta tarea) propagarán ese mismo
/// <c>login_at</c> sin modificarlo.
///
/// El test construye un <see cref="AuthService"/> real sobre EF Core InMemory (aislado por Guid),
/// con mocks de las dependencias que no intervienen en el login, siembra un usuario con una
/// contraseña conocida, ejecuta <c>LoginAsync</c> y decodifica el JWT devuelto para comparar
/// <c>login_at</c> con <c>iat</c>.
/// </summary>
public class AuthServiceLoginAtTests
{
    // Secreto de cifrado AES-256 en memoria (no interviene en el login, pero el servicio lo requiere).
    private const string EncryptionKeyName = "PASSWORD_RECOVERY_ENCRYPTION_KEY";
    private const string EncryptionKeyValue = "clave-de-prueba-unit-tests-login-at-32bytes++++++++++++++";

    // Contraseña conocida que cumple la Politica_Password (mayúscula, dígito, especial permitido).
    private const string KnownValidPassword = "Abc-123";

    // JwtSettings de prueba con los diccionarios por rol poblados, de modo que la vida del token
    // (invariante vida == ventana) se resuelva sin recurrir al fallback. Las claves usan el nombre
    // EXACTO de Usuario.Rol, incluida la "ñ" de "Dueño".
    private static readonly JwtSettings TestJwtSettings = new()
    {
        SecretKey = "ThisIsATestSecretKeyThatIsLongEnoughForHmacSha256Algorithm!!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpirationMinutes = 60,
        InactivityMinutesByRole = new Dictionary<string, int>
        {
            ["SuperAdmin"] = 15,
            ["Dueño"] = 30,
            ["Gerente"] = 30,
            ["Supervisor"] = 60,
            ["Bodeguero"] = 60,
            ["Cajero"] = 60,
        },
        InactivityMinutesDefault = 60,
    };

    // Contexto de prueba autocontenido: el AppDbContext InMemory y el AuthService bajo prueba.
    private sealed class ContextoPrueba : IDisposable
    {
        public required AppDbContext Db { get; init; }
        public required AuthService Servicio { get; init; }

        public void Dispose() => Db.Dispose();
    }

    // Construye un AuthService real sobre EF Core InMemory con cifrado AES-256 real y mocks de las
    // dependencias que no intervienen en el login (JTI blocklist y auditoría). El ITenantContext se
    // configura como SuperAdmin para no interferir con los filtros de tenant (el login ya usa
    // IgnoreQueryFilters internamente).
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
            Options.Create(TestJwtSettings),
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

    // Lee un claim numérico (epoch Unix) del JWT como long.
    private static long LeerClaimLong(JwtSecurityToken jwt, string claimType)
    {
        var claim = Assert.Single(jwt.Claims, c => c.Type == claimType);
        return long.Parse(claim.Value);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 4.1, 4.2: el token de login fija login_at = iat.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Al iniciar sesión con credenciales válidas, el token emitido incluye el claim <c>login_at</c>
    /// y su valor es exactamente igual al del claim <c>iat</c> (ambos corresponden al instante del
    /// login original, porque <c>LoginAsync</c> invoca <c>GenerateJwt</c> con <c>loginAtUnix: null</c>).
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Fact]
    public async Task Login_TokenIncluyeLoginAtIgualAIat()
    {
        using var ctx = CrearContexto();
        var usuario = SembrarUsuario(ctx.Db, "Cajero", comercioId: 100, sucursalId: 5, emailPrefix: "login");

        var resultado = await ctx.Servicio.LoginAsync(usuario.Email, KnownValidPassword);

        Assert.True(resultado.Success);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Token));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(resultado.Token);

        var iat = LeerClaimLong(jwt, JwtRegisteredClaimNames.Iat);
        var loginAt = LeerClaimLong(jwt, "login_at");

        // El primer token de la sesión fija login_at = iat (Req 4.1, 4.2).
        Assert.Equal(iat, loginAt);
    }

    /// <summary>
    /// El mismo flujo de login atiende al SuperAdmin (no hay emisión de token propia para ese rol),
    /// por lo que su primer token también fija <c>login_at = iat</c>. **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Fact]
    public async Task Login_SuperAdmin_TokenIncluyeLoginAtIgualAIat()
    {
        using var ctx = CrearContexto();
        // El SuperAdmin no tiene sucursal asociada; el claim sucursal_id no se emite en ese caso.
        var usuario = SembrarUsuario(ctx.Db, "SuperAdmin", comercioId: 1, sucursalId: null, emailPrefix: "superadmin");

        var resultado = await ctx.Servicio.LoginAsync(usuario.Email, KnownValidPassword);

        Assert.True(resultado.Success);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(resultado.Token);

        var iat = LeerClaimLong(jwt, JwtRegisteredClaimNames.Iat);
        var loginAt = LeerClaimLong(jwt, "login_at");

        Assert.Equal(iat, loginAt);
    }
}
