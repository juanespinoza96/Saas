using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Users;

/// <summary>
/// Integration tests for user CRUD operations via /api/tenants/usuarios.
/// Uses Plan Empresarial (unlimited users) to avoid hitting plan limits.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Users")]
public class UserCrudTests : IntegrationTestBase
{
    public UserCrudTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task Gerente_CreatesCajero_UserAppearsInList()
    {
        // Arrange: Create comercio (Empresarial), sucursal, and gerente
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var newUserEmail = $"cajero-{Guid.NewGuid():N}@test.com";
        var createRequest = new
        {
            Nombre = "Cajero Nuevo",
            Email = newUserEmail,
            Password = "Password123!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };

        // Act: Create user
        var createResponse = await Client.PostAsJsonAsync("/api/tenants/usuarios", createRequest);

        // Assert: 201 Created
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Act: List users
        var listResponse = await Client.GetAsync("/api/tenants/usuarios");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var users = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userList = users.EnumerateArray().ToList();

        // Assert: The new cajero appears in the list
        Assert.Contains(userList, u => u.GetProperty("email").GetString() == newUserEmail);
    }

    [DockerAvailableFact]
    public async Task DuplicateEmail_Returns409()
    {
        // Arrange: Create comercio, sucursal, and gerente
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var duplicateEmail = "test@test.com";

        var firstRequest = new
        {
            Nombre = "First User",
            Email = duplicateEmail,
            Password = "Password123!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };

        // Act: Create first user
        var firstResponse = await Client.PostAsJsonAsync("/api/tenants/usuarios", firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondRequest = new
        {
            Nombre = "Second User",
            Email = duplicateEmail,
            Password = "Password456!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };

        // Act: Create second user with same email
        var secondResponse = await Client.PostAsJsonAsync("/api/tenants/usuarios", secondRequest);

        // Assert: 409 Conflict with EMAIL_ALREADY_EXISTS
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var body = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EMAIL_ALREADY_EXISTS", body.GetProperty("code").GetString());
        Assert.Equal("El email ya está registrado.", body.GetProperty("error").GetString());
    }
}
