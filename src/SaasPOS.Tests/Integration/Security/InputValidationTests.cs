using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Security;

/// <summary>
/// Tests that InputValidationMiddleware detects SQL injection and XSS patterns
/// in request bodies (POST/PUT/PATCH) and query string parameters (GET/DELETE),
/// returning 400 Bad Request with DANGEROUS_INPUT code.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Security")]
public class InputValidationTests : IntegrationTestBase
{
    public InputValidationTests(PostgresFixture fixture) : base(fixture) { }

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

    // ─── SQL Injection patterns (Task 9.1) ─────────────────────────────────────

    [DockerAvailableFact]
    public async Task SqlInjection_DropTable_InBody_Returns400()
    {
        // Middleware runs before auth, so no authentication needed.
        // POST to login endpoint (AllowAnonymous) with SQL injection in password field.
        var request = new { email = "test@test.com", password = "'; DROP TABLE Usuarios; --" };
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DANGEROUS_INPUT", body.GetProperty("code").GetString());
        Assert.Equal("Input contains potentially dangerous content.", body.GetProperty("error").GetString());
    }

    [DockerAvailableFact]
    public async Task SqlInjection_UnionSelect_InBody_Returns400()
    {
        var request = new { email = "test@test.com", password = "' UNION SELECT * FROM Usuarios" };
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DANGEROUS_INPUT", body.GetProperty("code").GetString());
    }

    [DockerAvailableFact]
    public async Task SqlInjection_OrOneEqualsOne_InBody_Returns400()
    {
        var request = new { email = "test@test.com", password = "admin' OR 1=1 --" };
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DANGEROUS_INPUT", body.GetProperty("code").GetString());
    }

    [DockerAvailableFact]
    public async Task SqlInjection_InQueryString_Returns400()
    {
        // GET request with SQL injection in query parameter — needs auth for tenant endpoint
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var response = await Client.GetAsync("/api/tenants/productos?search='; DROP TABLE Usuarios; --");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DANGEROUS_INPUT", body.GetProperty("code").GetString());
    }

    // ─── XSS patterns (Task 9.2) ───────────────────────────────────────────────

    [DockerAvailableFact]
    public async Task XssPattern_ScriptTag_InBody_Returns400()
    {
        var request = new { email = "test@test.com", password = "<script>alert('xss')</script>" };
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DANGEROUS_INPUT", body.GetProperty("code").GetString());
        Assert.Equal("Input contains potentially dangerous content.", body.GetProperty("error").GetString());
    }

    [DockerAvailableFact]
    public async Task XssPattern_JavascriptProtocol_InBody_Returns400()
    {
        var request = new { email = "test@test.com", password = "javascript:alert(1)" };
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DANGEROUS_INPUT", body.GetProperty("code").GetString());
    }

    [DockerAvailableFact]
    public async Task XssPattern_OnError_InBody_Returns400()
    {
        var request = new { email = "test@test.com", password = "<img src=x onerror=alert(1)>" };
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DANGEROUS_INPUT", body.GetProperty("code").GetString());
    }

    [DockerAvailableFact]
    public async Task XssPattern_OnClick_InBody_Returns400()
    {
        var request = new { email = "test@test.com", password = "<div onclick=steal()>click</div>" };
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DANGEROUS_INPUT", body.GetProperty("code").GetString());
    }
}
