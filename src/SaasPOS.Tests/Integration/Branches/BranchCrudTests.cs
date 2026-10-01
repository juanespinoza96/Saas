using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Branches;

/// <summary>
/// Tests for branch (sucursal) CRUD operations including creation validation
/// and multi-tenant isolation.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Branches")]
public class BranchCrudTests : IntegrationTestBase
{
    public BranchCrudTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task Create_Branch_WithValidName_Returns201()
    {
        // Arrange: Create comercio with Empresarial plan (unlimited branches)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente (required by CanManageSucursales policy)
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new { Nombre = "Sucursal Nueva", Direccion = "Calle 123", Telefono = "0999" };

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/sucursales", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task Create_Branch_TenantIsolation()
    {
        // Arrange: Create two comercios (A and B) both Empresarial
        await using var db = CreateDbContext();
        var comercioA = await TestDataBuilder.Comercio().ConPlan(3).ConRazonSocial("Comercio A").CrearAsync(db);
        var sucursalA = await TestDataBuilder.Sucursal().EnComercio(comercioA.Id).ConNombre("Sucursal A").CrearAsync(db);
        var gerenteA = await TestDataBuilder.Usuario()
            .EnComercio(comercioA.Id).EnSucursal(sucursalA.Id).ConRol("Gerente").CrearAsync(db);

        var comercioB = await TestDataBuilder.Comercio().ConPlan(3).ConRazonSocial("Comercio B").CrearAsync(db);
        var sucursalB = await TestDataBuilder.Sucursal().EnComercio(comercioB.Id).ConNombre("Sucursal B").CrearAsync(db);

        // Authenticate as user from comercio A
        AuthenticateAs(gerenteA.Id, comercioA.Id, "Gerente");

        // Act: GET branches — should only see A's branches
        var response = await Client.GetAsync("/api/tenants/sucursales");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var branches = await response.Content.ReadFromJsonAsync<JsonElement>();
        var branchList = branches.EnumerateArray().ToList();

        // Should only see comercio A's branch
        Assert.All(branchList, branch =>
            Assert.Equal("Sucursal A", branch.GetProperty("nombre").GetString()));
        Assert.DoesNotContain(branchList, branch =>
            branch.GetProperty("nombre").GetString() == "Sucursal B");
    }

    [DockerAvailableFact]
    public async Task Create_Branch_NameRequired()
    {
        // Arrange: Create comercio with Empresarial plan
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Act: POST sucursal with empty Nombre
        var request = new { Nombre = "", Direccion = "Calle 123", Telefono = "0999" };
        var response = await Client.PostAsJsonAsync("/api/tenants/sucursales", request);

        // Assert: Should not be 201 (either 400 from model validation or 500 from DB constraint)
        Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);
    }
}
