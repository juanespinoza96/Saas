using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Pruebas de integración del endpoint de renovación silenciosa
/// <c>POST /api/tenants/auth/refresh</c> (tarea 7.4, valida las tareas 1–5 del backend).
///
/// Usan WebApplicationFactory + Testcontainers.PostgreSQL a través de <see cref="IntegrationTestBase"/>
/// y ejercitan el endpoint de extremo a extremo (AddJwtBearer → JtiValidationMiddleware →
/// RouteAuthorizationMiddleware → AuthController.Refresh → AuthService.RefreshTokenAsync):
///   1. Token válido → 200 + token renovado con MISMO <c>login_at</c> y NUEVO <c>jti</c>. _Requirements: 1.3, 4.4_
///   2. Token caducado → 401 (lo rechaza AddJwtBearer con ValidateLifetime/ClockSkew=0). _Requirements: 1.5_
///   3. Token revocado (logout previo del jti) → 401 (lo rechaza JtiValidationMiddleware). _Requirements: 1.5_
///   4. Dentro del tope absoluto → 200; fuera del tope absoluto → 401 aunque el token siga vigente. _Requirements: 1.4, 4.4_
///   5. Token SIN <c>sucursal_id</c> ni <c>must_change_password</c> → 200, y el token renovado
///      tampoco los incluye (preserva la ausencia). _Requirements: 1.3, 1.6, 4.4_
///
/// Nota: el usuario autenticado debe existir y estar Activo en la base real, porque
/// JtiValidationMiddleware verifica <c>Usuario.Activo</c> contra la DB (caché de 30 s). Por eso
/// cada caso siembra un Comercio + Sucursal + Usuario reales en el contenedor PostgreSQL.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "SessionRefresh")]
public class RefreshEndpointTests : IntegrationTestBase
{
    // Mismos valores que IntegrationTestBase/JwtTestHelper: la factory sobreescribe la SecretKey del
    // host con esta clave, de modo que los tokens que fabricamos aquí son aceptados por AddJwtBearer
    // y, a su vez, el token renovado que emite el host (AuthService.GenerateJwt) se firma con la misma
    // clave y podemos decodificarlo para leer sus claims.
    private const string JwtSecretKey = "IntegrationTestSecretKeyThatIsAtLeast32Characters!";
    private const string JwtIssuer = "saas-pos-api";
    private const string JwtAudience = "saas-pos-clients";

    // Rol estándar: ventana/vida 60 min, tope absoluto 720 min (12 h) según appsettings.json.
    private const string RolCajero = "Cajero";

    public RefreshEndpointTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
        await SeedPlansAsync();
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
    // 1) Token válido → 200 + token renovado con MISMO login_at y NUEVO jti (Req 1.3, 4.4).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un token vigente de un usuario Activo renueva correctamente: el endpoint responde 200 con
    /// <c>{ token }</c>, el token renovado propaga SIN cambios el <c>login_at</c> del token presentado
    /// y regenera el <c>jti</c> (y el <c>iat</c>). _Requirements: 1.3, 4.4_
    /// </summary>
    [DockerAvailableFact]
    public async Task Refresh_ConTokenValido_Retorna200_ConMismoLoginAt_YNuevoJti()
    {
        // Arrange: usuario Cajero real y Activo, y un token vigente con login_at = ahora.
        var (usuarioId, comercioId, sucursalId) = await SembrarUsuarioAsync(RolCajero);

        var loginAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (tokenOriginal, jtiOriginal) = GenerarToken(
            usuarioId, comercioId, RolCajero, loginAtUnix: loginAtUnix, sucursalId: sucursalId);

        AutenticarCon(tokenOriginal);

        // Act
        var respuesta = await Client.PostAsync("/api/tenants/auth/refresh", EmptyJson());

        // Assert: 200 con un token nuevo.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var tokenRenovado = await LeerTokenAsync(respuesta);
        Assert.False(string.IsNullOrWhiteSpace(tokenRenovado), "La renovación debe devolver un token.");

        var claims = LeerClaims(tokenRenovado!);

        // login_at se propaga EXACTAMENTE igual que en el token presentado.
        Assert.Equal(loginAtUnix.ToString(), ValorClaim(claims, "login_at"));

        // jti se regenera (debe ser distinto al original).
        var jtiRenovado = ValorClaim(claims, "jti");
        Assert.False(string.IsNullOrEmpty(jtiRenovado));
        Assert.NotEqual(jtiOriginal, jtiRenovado);

        // Los claims funcionales se preservan.
        Assert.Equal(usuarioId.ToString(), ValorClaim(claims, "sub"));
        Assert.Equal(comercioId.ToString(), ValorClaim(claims, "comercio_id"));
        Assert.Equal(RolCajero, ValorClaim(claims, "role"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2) Token caducado → 401 (AddJwtBearer, ValidateLifetime + ClockSkew=0) (Req 1.5).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un token cuya vida ya expiró es rechazado por la validación de vigencia de AddJwtBearer
    /// (ValidateLifetime = true, ClockSkew = TimeSpan.Zero) antes de llegar al handler → 401. _Requirements: 1.5_
    /// </summary>
    [DockerAvailableFact]
    public async Task Refresh_ConTokenCaducado_Retorna401()
    {
        // Arrange: usuario real Activo, pero un token ya caducado (exp en el pasado).
        var (usuarioId, comercioId, sucursalId) = await SembrarUsuarioAsync(RolCajero);

        var loginAtUnix = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeSeconds();
        var (tokenCaducado, _) = GenerarToken(
            usuarioId, comercioId, RolCajero,
            loginAtUnix: loginAtUnix,
            sucursalId: sucursalId,
            // Emitido hace una hora y ya expirado (dentro del tope absoluto, para aislar la causa = vida).
            notBefore: DateTime.UtcNow.AddMinutes(-60),
            expires: DateTime.UtcNow.AddMinutes(-1));

        AutenticarCon(tokenCaducado);

        // Act
        var respuesta = await Client.PostAsync("/api/tenants/auth/refresh", EmptyJson());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3) Token revocado (logout previo) → 401 (JtiValidationMiddleware) (Req 1.5).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tras hacer logout (que añade el <c>jti</c> del token a la blocklist), un intento de renovar
    /// con ESE MISMO token es rechazado por JtiValidationMiddleware con 401, sin llegar al handler. _Requirements: 1.5_
    /// </summary>
    [DockerAvailableFact]
    public async Task Refresh_ConTokenRevocado_Retorna401()
    {
        // Arrange: usuario real Activo y un token vigente.
        var (usuarioId, comercioId, sucursalId) = await SembrarUsuarioAsync(RolCajero);

        var loginAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (token, _) = GenerarToken(
            usuarioId, comercioId, RolCajero, loginAtUnix: loginAtUnix, sucursalId: sucursalId);

        AutenticarCon(token);

        // Revocar el token: logout añade su jti (y exp) a la blocklist en memoria.
        var respuestaLogout = await Client.PostAsync("/api/tenants/auth/logout", EmptyJson());
        Assert.Equal(HttpStatusCode.OK, respuestaLogout.StatusCode);

        // Act: intentar renovar con el mismo token ya revocado.
        var respuesta = await Client.PostAsync("/api/tenants/auth/refresh", EmptyJson());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4) Dentro del tope absoluto → 200; fuera del tope → 401 (Req 1.4, 4.4).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Con un <c>login_at</c> reciente (muy por debajo del tope absoluto de 720 min del Cajero), la
    /// renovación tiene éxito → 200. _Requirements: 1.4, 4.4_
    /// </summary>
    [DockerAvailableFact]
    public async Task Refresh_DentroDelTopeAbsoluto_Retorna200()
    {
        // Arrange: login_at hace 10 min (muy dentro del tope de 720 min); token vigente.
        var (usuarioId, comercioId, sucursalId) = await SembrarUsuarioAsync(RolCajero);

        var loginAtUnix = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var (token, _) = GenerarToken(
            usuarioId, comercioId, RolCajero, loginAtUnix: loginAtUnix, sucursalId: sucursalId);

        AutenticarCon(token);

        // Act
        var respuesta = await Client.PostAsync("/api/tenants/auth/refresh", EmptyJson());

        // Assert
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    /// <summary>
    /// Con un <c>login_at</c> anterior al tope absoluto (más de 720 min del Cajero), el servicio
    /// deniega la renovación aunque el token presentado siga vigente → 401. _Requirements: 1.4, 4.4_
    /// </summary>
    [DockerAvailableFact]
    public async Task Refresh_FueraDelTopeAbsoluto_Retorna401_AunConTokenVigente()
    {
        // Arrange: login_at hace 800 min (> 720 min del tope del Cajero), pero el token aún es válido
        // (exp en el futuro) para aislar que la causa del 401 es el tope absoluto, no la vida del token.
        var (usuarioId, comercioId, sucursalId) = await SembrarUsuarioAsync(RolCajero);

        var loginAtUnix = DateTimeOffset.UtcNow.AddMinutes(-800).ToUnixTimeSeconds();
        var (token, _) = GenerarToken(
            usuarioId, comercioId, RolCajero,
            loginAtUnix: loginAtUnix,
            sucursalId: sucursalId,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30));

        AutenticarCon(token);

        // Act
        var respuesta = await Client.PostAsync("/api/tenants/auth/refresh", EmptyJson());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 5) Token sin sucursal_id ni must_change_password → 200 y el renovado tampoco los incluye.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un token que NO trae <c>sucursal_id</c> ni <c>must_change_password</c> renueva sin lanzar (200),
    /// y el token renovado PRESERVA esa ausencia: no emite ninguno de los dos claims opcionales. _Requirements: 1.3, 1.6, 4.4_
    /// </summary>
    [DockerAvailableFact]
    public async Task Refresh_SinSucursalNiMustChange_Retorna200_YElRenovadoTampocoLosIncluye()
    {
        // Arrange: usuario Dueño sin sucursal asociada (no se envía sucursal_id); sin must_change_password.
        var (usuarioId, comercioId, _) = await SembrarUsuarioAsync("Dueño", conSucursal: false);

        var loginAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (token, _) = GenerarToken(
            usuarioId, comercioId, "Dueño",
            loginAtUnix: loginAtUnix,
            sucursalId: null,
            mustChangePassword: false);

        AutenticarCon(token);

        // Act
        var respuesta = await Client.PostAsync("/api/tenants/auth/refresh", EmptyJson());

        // Assert: 200 y el token renovado no incluye los opcionales ausentes.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var tokenRenovado = await LeerTokenAsync(respuesta);
        var claims = LeerClaims(tokenRenovado!);

        Assert.DoesNotContain(claims, c => c.Type == "sucursal_id");
        Assert.DoesNotContain(claims, c => c.Type == "must_change_password");

        // El login_at sigue propagándose igual.
        Assert.Equal(loginAtUnix.ToString(), ValorClaim(claims, "login_at"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra un Comercio (Plan Empresarial), opcionalmente una Sucursal y un Usuario Activo del rol
    /// indicado, usando datos reales en el contenedor PostgreSQL. Devuelve los identificadores que el
    /// token debe portar. El usuario DEBE existir y estar Activo para pasar JtiValidationMiddleware.
    /// </summary>
    private async Task<(int usuarioId, int comercioId, int? sucursalId)> SembrarUsuarioAsync(
        string rol, bool conSucursal = true)
    {
        await using var db = CreateDbContext();

        var comercio = await TestDataBuilder.Comercio()
            .ConRazonSocial($"Comercio Refresh {Guid.NewGuid():N}")
            .ConPlan(3)
            .CrearAsync(db);

        int? sucursalId = null;
        var builder = TestDataBuilder.Usuario().EnComercio(comercio.Id).ConRol(rol);

        if (conSucursal)
        {
            var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
            sucursalId = sucursal.Id;
            builder = builder.EnSucursal(sucursal.Id);
        }

        var usuario = await builder.CrearAsync(db);
        return (usuario.Id, comercio.Id, sucursalId);
    }

    /// <summary>
    /// Fabrica un JWT firmado con la clave de pruebas (aceptada por el host) incluyendo los claims que
    /// el endpoint de refresh necesita: <c>sub</c>, <c>comercio_id</c>, <c>role</c>, <c>jti</c>,
    /// <c>iat</c>, <c>login_at</c> y, opcionalmente, <c>sucursal_id</c> y <c>must_change_password</c>.
    /// Devuelve el token serializado y el <c>jti</c> emitido (para comparar contra el renovado).
    /// </summary>
    private static (string token, string jti) GenerarToken(
        int usuarioId,
        int comercioId,
        string role,
        long loginAtUnix,
        int? sucursalId = null,
        bool mustChangePassword = false,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var ahora = notBefore ?? DateTime.UtcNow;
        var iatUnix = new DateTimeOffset(DateTime.SpecifyKind(ahora, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var jti = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new("sub", usuarioId.ToString()),
            new("comercio_id", comercioId.ToString()),
            new("role", role),
            new("jti", jti),
            new("iat", iatUnix.ToString(), ClaimValueTypes.Integer64),
            new("login_at", loginAtUnix.ToString(), ClaimValueTypes.Integer64)
        };

        // Opcionales: solo se emiten cuando tienen valor / son true (igual que AuthService.GenerateJwt).
        if (sucursalId.HasValue)
            claims.Add(new Claim("sucursal_id", sucursalId.Value.ToString()));
        if (mustChangePassword)
            claims.Add(new Claim("must_change_password", "true"));

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            notBefore: ahora,
            expires: expires ?? DateTime.UtcNow.AddMinutes(60),
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), jti);
    }

    private void AutenticarCon(string token)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Cuerpo JSON vacío para los POST (evita 415 en clientes que exigen Content-Type).</summary>
    private static HttpContent EmptyJson() => JsonContent.Create(new { });

    /// <summary>Lee el token del cuerpo 200 (<c>{ "token": "..." }</c>).</summary>
    private static async Task<string?> LeerTokenAsync(HttpResponseMessage respuesta)
    {
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        return cuerpo.GetProperty("token").GetString();
    }

    /// <summary>Decodifica (sin validar) los claims de un JWT para inspeccionarlos en los asserts.</summary>
    private static IEnumerable<Claim> LeerClaims(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims;

    private static string? ValorClaim(IEnumerable<Claim> claims, string tipo) =>
        claims.FirstOrDefault(c => c.Type == tipo)?.Value;
}
