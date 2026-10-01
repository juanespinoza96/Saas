using System.Net;
using System.Text.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.MultiTenant;

/// <summary>
/// Tests that verify multi-tenant data isolation: user from comercio A
/// never sees data belonging to comercio B via tenant-scoped endpoints.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "MultiTenant")]
public class DataIsolationTests : IntegrationTestBase
{
    public DataIsolationTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task UserA_NeverSeesComercioB_Productos()
    {
        // Arrange: Create two comercios with products
        await using var db = CreateDbContext();

        var comercioA = await TestDataBuilder.Comercio().ConRazonSocial("Comercio A").ConPlan(1).CrearAsync(db);
        var comercioB = await TestDataBuilder.Comercio().ConRazonSocial("Comercio B").ConPlan(1).CrearAsync(db);

        var sucursalA = await TestDataBuilder.Sucursal().EnComercio(comercioA.Id).ConNombre("Sucursal A").CrearAsync(db);
        var sucursalB = await TestDataBuilder.Sucursal().EnComercio(comercioB.Id).ConNombre("Sucursal B").CrearAsync(db);

        var userA = await TestDataBuilder.Usuario().EnComercio(comercioA.Id).EnSucursal(sucursalA.Id).ConRol("Gerente").CrearAsync(db);

        // Create products for both comercios
        var productoA1 = await TestDataBuilder.Producto().EnComercio(comercioA.Id).ConNombre("Producto A1").CrearAsync(db);
        var productoA2 = await TestDataBuilder.Producto().EnComercio(comercioA.Id).ConNombre("Producto A2").CrearAsync(db);
        var productoB1 = await TestDataBuilder.Producto().EnComercio(comercioB.Id).ConNombre("Producto B1").CrearAsync(db);
        var productoB2 = await TestDataBuilder.Producto().EnComercio(comercioB.Id).ConNombre("Producto B2").CrearAsync(db);

        // Act: Authenticate as user from comercio A
        AuthenticateAs(userA.Id, comercioA.Id, "Gerente");
        var response = await Client.GetAsync("/api/tenants/productos");

        // Assert: Only sees comercio A's products
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<JsonElement>(content);
        var array = items.EnumerateArray().ToList();

        // All returned items should belong to comercio A
        Assert.True(array.Count >= 2, "Should return at least 2 products from comercio A");

        var names = array.Select(e => e.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("Producto A1", names);
        Assert.Contains("Producto A2", names);
        Assert.DoesNotContain("Producto B1", names);
        Assert.DoesNotContain("Producto B2", names);
    }

    [DockerAvailableFact]
    public async Task UserA_NeverSeesComercioB_Sucursales()
    {
        // Arrange: Create two comercios with sucursales
        await using var db = CreateDbContext();

        var comercioA = await TestDataBuilder.Comercio().ConRazonSocial("Comercio A").ConPlan(3).CrearAsync(db);
        var comercioB = await TestDataBuilder.Comercio().ConRazonSocial("Comercio B").ConPlan(3).CrearAsync(db);

        var sucursalA1 = await TestDataBuilder.Sucursal().EnComercio(comercioA.Id).ConNombre("Sucursal A1").CrearAsync(db);
        var sucursalA2 = await TestDataBuilder.Sucursal().EnComercio(comercioA.Id).ConNombre("Sucursal A2").CrearAsync(db);
        var sucursalB1 = await TestDataBuilder.Sucursal().EnComercio(comercioB.Id).ConNombre("Sucursal B1").CrearAsync(db);

        var userA = await TestDataBuilder.Usuario().EnComercio(comercioA.Id).EnSucursal(sucursalA1.Id).ConRol("Gerente").CrearAsync(db);

        // Act: Authenticate as user from comercio A
        AuthenticateAs(userA.Id, comercioA.Id, "Gerente");
        var response = await Client.GetAsync("/api/tenants/sucursales");

        // Assert: Only sees comercio A's sucursales
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<JsonElement>(content);
        var array = items.EnumerateArray().ToList();

        var names = array.Select(e => e.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("Sucursal A1", names);
        Assert.Contains("Sucursal A2", names);
        Assert.DoesNotContain("Sucursal B1", names);
    }

    [DockerAvailableFact]
    public async Task UserB_NeverSeesComercioA_Clientes()
    {
        // Arrange: Create two comercios with clients
        await using var db = CreateDbContext();

        var comercioA = await TestDataBuilder.Comercio().ConRazonSocial("Comercio A").ConPlan(1).CrearAsync(db);
        var comercioB = await TestDataBuilder.Comercio().ConRazonSocial("Comercio B").ConPlan(1).CrearAsync(db);

        var sucursalA = await TestDataBuilder.Sucursal().EnComercio(comercioA.Id).ConNombre("Sucursal A").CrearAsync(db);
        var sucursalB = await TestDataBuilder.Sucursal().EnComercio(comercioB.Id).ConNombre("Sucursal B").CrearAsync(db);

        var userB = await TestDataBuilder.Usuario().EnComercio(comercioB.Id).EnSucursal(sucursalB.Id).ConRol("Gerente").CrearAsync(db);

        // Create clients for both comercios
        var clienteA = await TestDataBuilder.Cliente().EnComercio(comercioA.Id).ConNombre("Cliente de A").CrearAsync(db);
        var clienteB = await TestDataBuilder.Cliente().EnComercio(comercioB.Id).ConNombre("Cliente de B").CrearAsync(db);

        // Act: Authenticate as user from comercio B
        AuthenticateAs(userB.Id, comercioB.Id, "Gerente");
        var response = await Client.GetAsync("/api/tenants/clientes");

        // Assert: Only sees comercio B's clients
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<JsonElement>(content);

        // The clientes endpoint returns { items: [...], total, page, pageSize }
        var items = body.GetProperty("items").EnumerateArray().ToList();

        var names = items.Select(e => e.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("Cliente de B", names);
        Assert.DoesNotContain("Cliente de A", names);
    }

    [DockerAvailableFact]
    public async Task AllTenantEndpoints_NeverReturnCrossTenantData()
    {
        // Arrange: Create two comercios with data in all endpoints
        await using var db = CreateDbContext();

        var comercioA = await TestDataBuilder.Comercio().ConRazonSocial("Comercio A").ConPlan(3).CrearAsync(db);
        var comercioB = await TestDataBuilder.Comercio().ConRazonSocial("Comercio B").ConPlan(3).CrearAsync(db);

        var sucursalA = await TestDataBuilder.Sucursal().EnComercio(comercioA.Id).ConNombre("Suc-A").CrearAsync(db);
        var sucursalB = await TestDataBuilder.Sucursal().EnComercio(comercioB.Id).ConNombre("Suc-B").CrearAsync(db);

        var userA = await TestDataBuilder.Usuario().EnComercio(comercioA.Id).EnSucursal(sucursalA.Id).ConRol("Gerente").CrearAsync(db);

        // Seed data for both comercios
        await TestDataBuilder.Producto().EnComercio(comercioA.Id).ConNombre("ProdA").CrearAsync(db);
        await TestDataBuilder.Producto().EnComercio(comercioB.Id).ConNombre("ProdB").CrearAsync(db);

        await TestDataBuilder.Categoria().EnComercio(comercioA.Id).ConNombre("CatA").CrearAsync(db);
        await TestDataBuilder.Categoria().EnComercio(comercioB.Id).ConNombre("CatB").CrearAsync(db);

        await TestDataBuilder.Cliente().EnComercio(comercioA.Id).ConNombre("ClienteA").CrearAsync(db);
        await TestDataBuilder.Cliente().EnComercio(comercioB.Id).ConNombre("ClienteB").CrearAsync(db);

        // Act & Assert: Authenticate as user from comercio A
        AuthenticateAs(userA.Id, comercioA.Id, "Gerente");

        // Test productos endpoint
        var productosResponse = await Client.GetAsync("/api/tenants/productos");
        Assert.Equal(HttpStatusCode.OK, productosResponse.StatusCode);
        var productosContent = await productosResponse.Content.ReadAsStringAsync();
        var productosArray = JsonSerializer.Deserialize<JsonElement>(productosContent).EnumerateArray().ToList();
        var prodNames = productosArray.Select(e => e.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("ProdA", prodNames);
        Assert.DoesNotContain("ProdB", prodNames);

        // Test sucursales endpoint
        var sucursalesResponse = await Client.GetAsync("/api/tenants/sucursales");
        Assert.Equal(HttpStatusCode.OK, sucursalesResponse.StatusCode);
        var sucursalesContent = await sucursalesResponse.Content.ReadAsStringAsync();
        var sucursalesArray = JsonSerializer.Deserialize<JsonElement>(sucursalesContent).EnumerateArray().ToList();
        var sucNames = sucursalesArray.Select(e => e.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("Suc-A", sucNames);
        Assert.DoesNotContain("Suc-B", sucNames);

        // Test clientes endpoint
        var clientesResponse = await Client.GetAsync("/api/tenants/clientes");
        Assert.Equal(HttpStatusCode.OK, clientesResponse.StatusCode);
        var clientesContent = await clientesResponse.Content.ReadAsStringAsync();
        var clientesBody = JsonSerializer.Deserialize<JsonElement>(clientesContent);
        var clientesItems = clientesBody.GetProperty("items").EnumerateArray().ToList();
        var clienteNames = clientesItems.Select(e => e.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("ClienteA", clienteNames);
        Assert.DoesNotContain("ClienteB", clienteNames);

        // Test categorias endpoint
        var categoriasResponse = await Client.GetAsync("/api/tenants/categorias");
        Assert.Equal(HttpStatusCode.OK, categoriasResponse.StatusCode);
        var categoriasContent = await categoriasResponse.Content.ReadAsStringAsync();
        var categoriasArray = JsonSerializer.Deserialize<JsonElement>(categoriasContent).EnumerateArray().ToList();
        var catNames = categoriasArray.Select(e => e.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("CatA", catNames);
        Assert.DoesNotContain("CatB", catNames);
    }
}
