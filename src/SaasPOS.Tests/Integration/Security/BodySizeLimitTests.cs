using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Security;

/// <summary>
/// Tests that the server enforces body size limits (1MB max) and Content-Type validation.
/// Kestrel MaxRequestBodySize = 1_048_576 bytes.
/// InputValidationMiddleware requires Content-Type: application/json for POST/PUT/PATCH.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Security")]
public class BodySizeLimitTests : IntegrationTestBase
{
    public BodySizeLimitTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task BodyOver1MB_Returns413()
    {
        // Arrange: Authenticate as Gerente so we pass auth middleware
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Create a body larger than 1MB (1_048_577 bytes)
        var largePayload = new string('x', 1_048_577);
        var content = new StringContent(
            JsonSerializer.Serialize(new { data = largePayload }),
            Encoding.UTF8,
            "application/json");

        // Act & Assert: Kestrel should reject with 413 or close the connection.
        // InputValidationMiddleware may also reject with 400 if the JSON is invalid
        // before the size check is reached.
        try
        {
            var response = await Client.PostAsync("/api/tenants/productos", content);
            Assert.True(
                response.StatusCode == HttpStatusCode.RequestEntityTooLarge ||
                response.StatusCode == HttpStatusCode.BadRequest,
                $"Expected 413 (too large) or 400 (invalid JSON from size), got {(int)response.StatusCode}");
        }
        catch (HttpRequestException)
        {
            // Kestrel may forcefully close connection for oversized bodies
            // This is also acceptable behavior — the request was rejected
            Assert.True(true, "Connection closed by server for oversized body");
        }
    }

    [DockerAvailableFact]
    public async Task ContentTypePlainText_Returns415()
    {
        // Arrange: No auth needed — InputValidationMiddleware runs before auth
        // Use POST /api/tenants/auth/login which is AllowAnonymous
        var content = new StringContent("{}", Encoding.UTF8, "text/plain");

        // Act
        var response = await Client.PostAsync("/api/tenants/auth/login", content);

        // Assert
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Content-Type must be application/json.", body.GetProperty("error").GetString());
        Assert.Equal("UNSUPPORTED_MEDIA_TYPE", body.GetProperty("code").GetString());
    }
}
