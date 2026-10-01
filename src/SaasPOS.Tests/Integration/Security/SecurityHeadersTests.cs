using System.Net;
using System.Net.Http.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Security;

/// <summary>
/// Tests that SecurityHeadersMiddleware adds required security headers to every response:
/// X-Content-Type-Options: nosniff, X-Frame-Options: DENY,
/// Strict-Transport-Security (HSTS), and Referrer-Policy.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Security")]
public class SecurityHeadersTests : IntegrationTestBase
{
    public SecurityHeadersTests(PostgresFixture fixture) : base(fixture) { }

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

    [DockerAvailableFact]
    public async Task Response_ContainsXContentTypeOptions_Nosniff()
    {
        // Any response should include security headers — use login endpoint (AllowAnonymous)
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login",
            new { email = "nobody@test.com", password = "testpass" });

        Assert.True(
            response.Headers.TryGetValues("X-Content-Type-Options", out var values),
            "Response should contain X-Content-Type-Options header");
        Assert.Equal("nosniff", values!.First());
    }

    [DockerAvailableFact]
    public async Task Response_ContainsXFrameOptions_Deny()
    {
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login",
            new { email = "nobody@test.com", password = "testpass" });

        Assert.True(
            response.Headers.TryGetValues("X-Frame-Options", out var values),
            "Response should contain X-Frame-Options header");
        Assert.Equal("DENY", values!.First());
    }

    [DockerAvailableFact]
    public async Task Response_ContainsStrictTransportSecurity()
    {
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login",
            new { email = "nobody@test.com", password = "testpass" });

        Assert.True(
            response.Headers.TryGetValues("Strict-Transport-Security", out var values),
            "Response should contain Strict-Transport-Security header");

        var hsts = values!.First();
        Assert.Contains("max-age=31536000", hsts);
        Assert.Contains("includeSubDomains", hsts);
    }

    [DockerAvailableFact]
    public async Task Response_ContainsReferrerPolicy()
    {
        var response = await Client.PostAsJsonAsync("/api/tenants/auth/login",
            new { email = "nobody@test.com", password = "testpass" });

        Assert.True(
            response.Headers.TryGetValues("Referrer-Policy", out var values),
            "Response should contain Referrer-Policy header");
        Assert.Equal("strict-origin-when-cross-origin", values!.First());
    }

    [DockerAvailableFact]
    public async Task SecurityHeaders_PresentOnAuthenticatedEndpoints()
    {
        // Verify headers are also present on authenticated endpoint responses
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var response = await Client.GetAsync("/api/tenants/productos");

        // Headers should be present regardless of the response status code
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff));
        Assert.Equal("nosniff", nosniff!.First());

        Assert.True(response.Headers.TryGetValues("X-Frame-Options", out var frameOptions));
        Assert.Equal("DENY", frameOptions!.First());

        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var referrer));
        Assert.Equal("strict-origin-when-cross-origin", referrer!.First());
    }
}
