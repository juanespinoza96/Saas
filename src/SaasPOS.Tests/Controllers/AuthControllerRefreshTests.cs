using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SaasPOS.Api.Controllers;
using SaasPOS.Application.DTOs.Results;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Services;
using Xunit;

namespace SaasPOS.Tests.Controllers;

/// <summary>
/// Feature: sesion-inactividad-y-expiracion-por-rol, tarea 7.2.
/// Unit tests (xUnit + Moq) de la acción <c>AuthController.Refresh</c>.
///
/// Verifican el contrato de seguridad del endpoint de renovación silenciosa:
///   - Con el tipo concreto <see cref="JtiBlocklist"/>: si el corte masivo por usuario o por comercio
///     da positivo contra el <c>iat</c> presentado, se responde 401 SIN invocar <c>RefreshTokenAsync</c>
///     (Req 1.5, 4.5, 9.5).
///   - Cualquier claim OBLIGATORIO ausente o malformado (<c>sub</c>, <c>comercio_id</c>, <c>role</c>,
///     <c>login_at</c>, <c>iat</c>) → 401, nunca 500 (Req 8.7).
///   - Token válido dentro del tope absoluto → 200 con <c>{ token }</c> (Req 4.4).
///
/// El re-chequeo del corte masivo vive en el controlador con el tipo concreto <see cref="JtiBlocklist"/>
/// (sus métodos <c>IsUserBlocked</c>/<c>IsComercioBlocked</c> no existen en la interfaz). Esos métodos
/// solo leen de <see cref="IMemoryCache"/>, así que el SUT usa una caché REAL y un
/// <see cref="IServiceScopeFactory"/> simulado (los cortes masivos no tocan BD). El resto de
/// dependencias del controlador (lockout, tracker, scope factory, logger) son irrelevantes para
/// Refresh y se inyectan simuladas/nulas. <c>IAuthService</c> se simula con Moq para comprobar tanto
/// el resultado como que NO se le invoca cuando el corte masivo bloquea.
/// </summary>
public class AuthControllerRefreshTests
{
    private const int UserId = 42;
    private const int ComercioId = 7;
    private const string Role = "Cajero";

    /// <summary>
    /// Arnés autocontenido con el controlador cableado: el mock de <see cref="IAuthService"/> para
    /// verificar invocaciones, la caché real que respalda al <see cref="JtiBlocklist"/> concreto y el
    /// propio <see cref="JtiBlocklist"/> para disparar los cortes masivos desde los tests.
    /// </summary>
    private sealed class Arnes : IDisposable
    {
        public required Mock<IAuthService> AuthService { get; init; }
        public required MemoryCache Cache { get; init; }
        public required JtiBlocklist JtiBlocklist { get; init; }
        public required AuthController Controller { get; init; }

        public void Dispose() => Cache.Dispose();
    }

    // Construye el arnés con todas las dependencias del AuthController. Solo IAuthService y el
    // JtiBlocklist concreto son relevantes para Refresh; las demás son simuladas/nulas.
    private static Arnes CrearArnes()
    {
        var authService = new Mock<IAuthService>();

        // Caché real + scope factory simulado: los cortes masivos (BlockAllForUser/IsUserBlocked)
        // viven 100% en IMemoryCache y jamás abren un scope de BD, por lo que el factory no se ejerce.
        var cache = new MemoryCache(new MemoryCacheOptions());
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var jtiBlocklist = new JtiBlocklist(cache, scopeFactory.Object, NullLogger<JtiBlocklist>.Instance);

        var controller = new AuthController(
            authService.Object,
            jtiBlocklist,
            new Mock<IAccountLockoutService>().Object,
            new Mock<ILoginAttemptTracker>().Object,
            new Mock<IServiceScopeFactory>().Object,
            NullLogger<AuthController>.Instance);

        return new Arnes
        {
            AuthService = authService,
            Cache = cache,
            JtiBlocklist = jtiBlocklist,
            Controller = controller,
        };
    }

    // Epoch UTC en segundos del instante indicado (formato del claim iat/login_at).
    private static long UnixSeconds(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();

    /// <summary>
    /// Asigna al controlador un <see cref="ClaimsPrincipal"/> autenticado con los claims indicados.
    /// Simula el token ya validado por AddJwtBearer: la acción solo lee claims de <c>User</c>.
    /// Cada entrada (nombre, valor) se añade como claim; omitir una clave simula su ausencia.
    /// </summary>
    private static void AsignarClaims(AuthController controller, params (string Tipo, string Valor)[] claims)
    {
        var lista = claims.Select(c => new Claim(c.Tipo, c.Valor)).ToList();
        var identity = new ClaimsIdentity(lista, authenticationType: "TestJwt");
        var principal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };
    }

    // Conjunto de claims VÁLIDO y completo para un token dentro del tope. loginAt/iat se fijan "ahora".
    // El claim "role" se emite con ese nombre literal porque el controlador lo lee con
    // User.FindFirstValue("role") (no ClaimTypes.Role). "sub" se mapea a NameIdentifier por el handler JWT.
    private static (string, string)[] ClaimsValidos(long loginAtUnix, long iatUnix) => new[]
    {
        (ClaimTypes.NameIdentifier, UserId.ToString()),
        ("comercio_id", ComercioId.ToString()),
        ("role", Role),
        ("login_at", loginAtUnix.ToString()),
        ("iat", iatUnix.ToString()),
    };

    // ──────────────────────────────────────────────────────────────────────────────
    // Corte masivo (JtiBlocklist concreto): 401 SIN invocar RefreshTokenAsync
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_CorteMasivoPorUsuario_Retorna401SinInvocarServicio()
    {
        using var arnes = CrearArnes();

        // El token se emitió hace una hora; el corte masivo por usuario se activa "ahora",
        // de modo que IsUserBlocked(userId, iat) → true (iat <= instanteDeCorte).
        var iat = UnixSeconds(DateTime.UtcNow.AddHours(-1));
        AsignarClaims(arnes.Controller, ClaimsValidos(loginAtUnix: iat, iatUnix: iat));
        arnes.JtiBlocklist.BlockAllForUser(UserId);

        var result = await arnes.Controller.Refresh();

        Assert.IsType<UnauthorizedResult>(result);
        // Clave: el corte masivo niega la renovación ANTES de llamar al servicio (Req 1.5).
        arnes.AuthService.Verify(
            s => s.RefreshTokenAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<long>()),
            Times.Never);
    }

    [Fact]
    public async Task Refresh_CorteMasivoPorComercio_Retorna401SinInvocarServicio()
    {
        using var arnes = CrearArnes();

        var iat = UnixSeconds(DateTime.UtcNow.AddHours(-1));
        AsignarClaims(arnes.Controller, ClaimsValidos(loginAtUnix: iat, iatUnix: iat));
        arnes.JtiBlocklist.BlockAllForComercio(ComercioId);

        var result = await arnes.Controller.Refresh();

        Assert.IsType<UnauthorizedResult>(result);
        arnes.AuthService.Verify(
            s => s.RefreshTokenAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<long>()),
            Times.Never);
    }

    [Fact]
    public async Task Refresh_CorteMasivoAnteriorAlToken_NoBloquea_Retorna200()
    {
        using var arnes = CrearArnes();

        // El corte masivo se registra PRIMERO y el token se "emite" DESPUÉS (iat en el futuro
        // respecto al corte): IsUserBlocked exige iat <= instanteDeCorte, así que NO bloquea.
        arnes.JtiBlocklist.BlockAllForUser(UserId);
        var iat = UnixSeconds(DateTime.UtcNow.AddMinutes(5));
        AsignarClaims(arnes.Controller, ClaimsValidos(loginAtUnix: iat, iatUnix: iat));

        arnes.AuthService
            .Setup(s => s.RefreshTokenAsync(UserId, ComercioId, Role, null, false, iat))
            .ReturnsAsync(RefreshResult.Ok("token-renovado"));

        var result = await arnes.Controller.Refresh();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("token-renovado", LeerToken(ok.Value));
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Claims obligatorios ausentes/malformados → 401 (nunca 500)
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_LoginAtAusente_Retorna401()
    {
        using var arnes = CrearArnes();
        var iat = UnixSeconds(DateTime.UtcNow);

        // Mismo conjunto válido pero SIN el claim login_at.
        AsignarClaims(arnes.Controller,
            (ClaimTypes.NameIdentifier, UserId.ToString()),
            ("comercio_id", ComercioId.ToString()),
            ("role", Role),
            ("iat", iat.ToString()));

        var result = await arnes.Controller.Refresh();

        Assert.IsType<UnauthorizedResult>(result);
        VerificarServicioNoInvocado(arnes);
    }

    [Fact]
    public async Task Refresh_LoginAtMalformado_Retorna401()
    {
        using var arnes = CrearArnes();
        var iat = UnixSeconds(DateTime.UtcNow);

        AsignarClaims(arnes.Controller,
            (ClaimTypes.NameIdentifier, UserId.ToString()),
            ("comercio_id", ComercioId.ToString()),
            ("role", Role),
            ("login_at", "no-es-un-numero"),
            ("iat", iat.ToString()));

        var result = await arnes.Controller.Refresh();

        Assert.IsType<UnauthorizedResult>(result);
        VerificarServicioNoInvocado(arnes);
    }

    // Un test parametrizado por cada claim obligatorio: ausente o malformado → 401, nunca 500.
    public static IEnumerable<object[]> ClaimsObligatorios()
    {
        foreach (var claim in new[] { ClaimTypes.NameIdentifier, "comercio_id", "role", "login_at", "iat" })
        {
            yield return new object[] { claim, true };   // ausente
            yield return new object[] { claim, false };  // malformado
        }
    }

    [Theory]
    [MemberData(nameof(ClaimsObligatorios))]
    public async Task Refresh_ClaimObligatorioAusenteOMalformado_Retorna401(string claimOmitido, bool ausente)
    {
        using var arnes = CrearArnes();
        var now = UnixSeconds(DateTime.UtcNow);

        // Valor malformado por tipo: numéricos → texto no numérico; role → cadena vacía (inválido).
        var valorInvalido = claimOmitido == "role" ? string.Empty : "xx-no-numero";

        // Parte de un conjunto válido y, para el claim bajo prueba, lo omite (ausente) o lo corrompe.
        var baseClaims = new List<(string Tipo, string Valor)>
        {
            (ClaimTypes.NameIdentifier, UserId.ToString()),
            ("comercio_id", ComercioId.ToString()),
            ("role", Role),
            ("login_at", now.ToString()),
            ("iat", now.ToString()),
        };

        var claims = baseClaims
            .Where(c => !(ausente && c.Tipo == claimOmitido))
            .Select(c => c.Tipo == claimOmitido && !ausente ? (c.Tipo, valorInvalido) : c)
            .ToArray();

        AsignarClaims(arnes.Controller, claims);

        var result = await arnes.Controller.Refresh();

        // Siempre 401: ninguna rama lanza (no hay 500) y el servicio no se invoca.
        Assert.IsType<UnauthorizedResult>(result);
        VerificarServicioNoInvocado(arnes);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Camino feliz: token válido dentro del tope → 200 con { token }
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_TokenValidoDentroDelTope_Retorna200ConToken()
    {
        using var arnes = CrearArnes();

        var now = UnixSeconds(DateTime.UtcNow);
        AsignarClaims(arnes.Controller, ClaimsValidos(loginAtUnix: now, iatUnix: now));

        arnes.AuthService
            .Setup(s => s.RefreshTokenAsync(UserId, ComercioId, Role, null, false, now))
            .ReturnsAsync(RefreshResult.Ok("nuevo-jwt"));

        var result = await arnes.Controller.Refresh();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("nuevo-jwt", LeerToken(ok.Value));
        arnes.AuthService.Verify(
            s => s.RefreshTokenAsync(UserId, ComercioId, Role, null, false, now),
            Times.Once);
    }

    [Fact]
    public async Task Refresh_ServicioFallaPorTopeSuperado_Retorna401()
    {
        using var arnes = CrearArnes();

        var now = UnixSeconds(DateTime.UtcNow);
        AsignarClaims(arnes.Controller, ClaimsValidos(loginAtUnix: now, iatUnix: now));

        // El tope absoluto lo valida el servicio: Fail → el controlador lo traduce a 401.
        arnes.AuthService
            .Setup(s => s.RefreshTokenAsync(UserId, ComercioId, Role, null, false, now))
            .ReturnsAsync(RefreshResult.Fail());

        var result = await arnes.Controller.Refresh();

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Refresh_ClaimsOpcionalesPresentes_SePropaganAlServicio()
    {
        using var arnes = CrearArnes();

        var now = UnixSeconds(DateTime.UtcNow);
        // Conjunto válido + opcionales: sucursal_id = 3, must_change_password = "true".
        AsignarClaims(arnes.Controller,
            (ClaimTypes.NameIdentifier, UserId.ToString()),
            ("comercio_id", ComercioId.ToString()),
            ("role", Role),
            ("login_at", now.ToString()),
            ("iat", now.ToString()),
            ("sucursal_id", "3"),
            ("must_change_password", "true"));

        arnes.AuthService
            .Setup(s => s.RefreshTokenAsync(UserId, ComercioId, Role, 3, true, now))
            .ReturnsAsync(RefreshResult.Ok("jwt-con-opcionales"));

        var result = await arnes.Controller.Refresh();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("jwt-con-opcionales", LeerToken(ok.Value));
        arnes.AuthService.Verify(
            s => s.RefreshTokenAsync(UserId, ComercioId, Role, 3, true, now),
            Times.Once);
    }

    // Verifica que RefreshTokenAsync NO fue invocado (para las ramas que retornan 401 antes del servicio).
    private static void VerificarServicioNoInvocado(Arnes arnes) =>
        arnes.AuthService.Verify(
            s => s.RefreshTokenAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<long>()),
            Times.Never);

    // Lee la propiedad "token" del objeto anónimo { token } devuelto en el OkObjectResult.
    private static string? LeerToken(object? value)
    {
        Assert.NotNull(value);
        var prop = value!.GetType().GetProperty("token");
        Assert.NotNull(prop);
        return prop!.GetValue(value) as string;
    }
}
