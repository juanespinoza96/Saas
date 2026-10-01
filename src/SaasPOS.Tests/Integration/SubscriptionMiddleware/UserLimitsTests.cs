using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.SubscriptionMiddleware;

/// <summary>
/// Tests that the subscription middleware enforces user limits per plan.
/// Plan Básico: max 2 users per comercio.
/// Plan Intermedio: max 3 users per sucursal.
/// Plan Empresarial: unlimited users.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "SubscriptionMiddleware")]
public class UserLimitsTests : IntegrationTestBase
{
    public UserLimitsTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task PlanBasico_RejectsThirdUser_Returns403()
    {
        // Arrange: Create comercio with Plan Básico (limit = 2 users per comercio)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(1).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create 2 users (at limit)
        var user1 = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);
        var user2 = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(user1.Id, comercio.Id, "Gerente");

        var request = new
        {
            Nombre = "Third User",
            Email = $"third-{Guid.NewGuid():N}@test.com",
            Password = "Password123!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/usuarios", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task PlanIntermedio_RejectsFourthUserPerBranch_Returns403()
    {
        // Arrange: Create comercio with Plan Intermedio (limit = 3 users per sucursal)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(2).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create 3 users in the same sucursal (at limit)
        var user1 = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);
        var user2 = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);
        var user3 = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(user1.Id, comercio.Id, "Gerente");

        var request = new
        {
            Nombre = "Fourth User",
            Email = $"fourth-{Guid.NewGuid():N}@test.com",
            Password = "Password123!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/usuarios", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task PlanEmpresarial_AllowsUnlimitedUsers_Returns201()
    {
        // Arrange: Create comercio with Plan Empresarial (unlimited users)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create 10 users to prove no limit
        Usuario? gerente = null;
        for (int i = 0; i < 10; i++)
        {
            var rol = i == 0 ? "Gerente" : "Cajero";
            var user = await TestDataBuilder.Usuario()
                .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol(rol).CrearAsync(db);
            if (i == 0) gerente = user;
        }

        // Authenticate as Gerente
        AuthenticateAs(gerente!.Id, comercio.Id, "Gerente");

        var request = new
        {
            Nombre = "Eleventh User",
            Email = $"eleventh-{Guid.NewGuid():N}@test.com",
            Password = "Password123!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/usuarios", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task UserLimitExceeded_ResponseIncludesDescriptiveMessage()
    {
        // Arrange: Same as Plan Básico at limit
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(1).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create 2 users (at limit)
        var user1 = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);
        var user2 = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(user1.Id, comercio.Id, "Gerente");

        var request = new
        {
            Nombre = "Excess User",
            Email = $"excess-{Guid.NewGuid():N}@test.com",
            Password = "Password123!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/usuarios", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Se ha alcanzado el límite de usuarios del plan.", body.GetProperty("error").GetString());
        Assert.Equal("USER_LIMIT_EXCEEDED", body.GetProperty("code").GetString());
    }
}
