using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Users;

/// <summary>
/// Integration tests for user deactivation via PATCH /api/tenants/usuarios/{id}/desactivar.
/// Verifies Activo flag is set to false and JTI blocklist invalidates sessions.
/// Uses Plan Empresarial (unlimited users) to avoid hitting plan limits.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Users")]
public class UserDeactivationTests : IntegrationTestBase
{
    public UserDeactivationTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task Desactivar_SetsActivoFalse()
    {
        // Arrange: Create comercio, sucursal, gerente, and cajero
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);
        var cajero = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Act: Deactivate the cajero
        var response = await Client.PatchAsync(
            $"/api/tenants/usuarios/{cajero.Id}/desactivar",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        // Assert: 200 OK
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Usuario desactivado exitosamente.", body.GetProperty("message").GetString());

        // Verify in DB that Activo is false
        await using var verifyDb = CreateDbContext();
        var updatedCajero = await verifyDb.Usuarios
            .IgnoreQueryFilters()
            .FirstAsync(u => u.Id == cajero.Id);
        Assert.False(updatedCajero.Activo);
    }

    [DockerAvailableFact]
    public async Task DeactivatedUser_TokenReturns401()
    {
        // Arrange: Create comercio, sucursal, gerente, and cajero
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);
        var cajero = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

        // Step 1: Generate a token for the cajero BEFORE deactivation
        // This token has iat = now, so when BlockAllForUser sets blockedAt = now,
        // the condition tokenIssuedAt <= blockedAt will be true.
        var cajeroToken = GenerateTestToken(cajero.Id, comercio.Id, "Cajero");

        // Step 2: Authenticate as Gerente and deactivate the cajero
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");
        var deactivateResponse = await Client.PatchAsync(
            $"/api/tenants/usuarios/{cajero.Id}/desactivar",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        // Step 3: Use the cajero's token (generated before deactivation) to access an endpoint
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cajeroToken);

        var response = await Client.GetAsync("/api/tenants/productos");

        // Assert: 401 Unauthorized — JTI blocklist rejects the token
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
