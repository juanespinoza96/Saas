using System.Net;
using System.Net.Http.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.SubscriptionMiddleware;

/// <summary>
/// Tests that the subscription middleware enforces branch (sucursal) limits per plan.
/// Plan Básico: max 1 sucursal per comercio.
/// Plan Intermedio / Empresarial: unlimited sucursales.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "SubscriptionMiddleware")]
public class BranchLimitsTests : IntegrationTestBase
{
    public BranchLimitsTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task PlanBasico_RejectsSecondBranch_Returns403()
    {
        // Arrange: Create comercio with Plan Básico (limit = 1 sucursal)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(1).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create a Gerente user for authentication
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente (required by CanManageSucursales policy)
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new { Nombre = "Sucursal 2", Direccion = "Calle 2", Telefono = "0999999999" };

        // Act: Try to create a second sucursal (exceeds limit of 1)
        var response = await Client.PostAsJsonAsync("/api/tenants/sucursales", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task PlanIntermedio_AllowsMultipleBranches_Returns201()
    {
        // Arrange: Create comercio with Plan Intermedio (unlimited sucursales)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(2).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create a Gerente user for authentication
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new { Nombre = "Sucursal 2", Direccion = "Calle 2", Telefono = "0999999999" };

        // Act: Create a second sucursal (should succeed — no limit)
        var response = await Client.PostAsJsonAsync("/api/tenants/sucursales", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
