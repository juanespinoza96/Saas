using System.Net;
using System.Net.Http.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Security;

/// <summary>
/// Tests that rate limiting policies enforce request thresholds correctly.
/// Global: 100 requests/min per IP (FixedWindow).
/// Login: 5 attempts/min per IP via LoginPolicy.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Security")]
public class RateLimitingTests : IntegrationTestBase
{
    public RateLimitingTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task MoreThan100Requests_Returns429()
    {
        // Arrange: Create comercio and authenticate as Gerente
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Act: Send 101 requests to exceed the 100 req/min global limit
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 101; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            responses.Add(response);
        }

        // Assert: At least the 101st request should be rate-limited (429)
        var rateLimitedResponses = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.NotEmpty(rateLimitedResponses);

        // Verify the rate-limited response has Retry-After header
        var firstRateLimited = rateLimitedResponses.First();
        Assert.True(
            firstRateLimited.Headers.Contains("Retry-After"),
            "Rate-limited response should include Retry-After header");
    }

    [DockerAvailableFact]
    public async Task MoreThan5LoginAttempts_Returns429()
    {
        // Arrange: No authentication needed — login is AllowAnonymous
        var loginBody = new { email = "wrong@test.com", password = "wrong" };

        // Act: Send 6 login attempts with wrong credentials
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 6; i++)
        {
            var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", loginBody);
            responses.Add(response);
        }

        // Assert: First 5 should get 401 (wrong credentials), 6th should get 429
        var rateLimitedResponses = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.NotEmpty(rateLimitedResponses);

        // The first 5 responses should be 401 (unauthorized — wrong credentials)
        var earlyResponses = responses.Take(5).ToList();
        Assert.All(earlyResponses, r =>
            Assert.True(
                r.StatusCode == HttpStatusCode.Unauthorized || r.StatusCode == HttpStatusCode.TooManyRequests,
                $"Expected 401 or 429, got {(int)r.StatusCode}"));

        // The last response should be 429
        Assert.Equal(HttpStatusCode.TooManyRequests, responses.Last().StatusCode);
    }
}
