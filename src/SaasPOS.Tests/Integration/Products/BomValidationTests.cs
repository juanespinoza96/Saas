using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaasPOS.Application.DTOs;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Products;

/// <summary>
/// Tests for BOM (Bill of Materials) validation:
/// - Ensamblado product cannot be created directly without recipe
/// - Empty recipe list is rejected
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Products")]
public class BomValidationTests : IntegrationTestBase
{
    public BomValidationTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task Create_Ensamblado_WithoutReceta_Returns400()
    {
        // Arrange
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Act: Try to create Ensamblado directly (no recipe)
        var request = new CreateProductoRequest(
            Nombre: "Hamburguesa Completa",
            TipoArticulo: "Ensamblado",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Unidades",
            CostoProduccion: 3.50m,
            PrecioLista: 7.00m,
            PrecioMinimo: 5.50m);

        var response = await Client.PostAsJsonAsync("/api/tenants/productos", request);

        // Assert: Should be rejected
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ENSAMBLADO_REQUIRES_RECETA", body.GetProperty("code").GetString());
    }

    [DockerAvailableFact]
    public async Task SetReceta_EmptyList_Returns400()
    {
        // Arrange: Create a Venta Directa product first
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var createRequest = new CreateProductoRequest(
            Nombre: "Producto Sin Receta",
            TipoArticulo: "Venta Directa",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Unidades",
            CostoProduccion: 1.00m,
            PrecioLista: 3.00m,
            PrecioMinimo: 2.00m);

        var createResponse = await Client.PostAsJsonAsync("/api/tenants/productos", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var dto = await createResponse.Content.ReadFromJsonAsync<ProductoDto>();

        // Act: Try to set empty recipe
        var emptyReceta = Array.Empty<object>();
        var response = await Client.PostAsJsonAsync($"/api/tenants/productos/{dto!.Id}/receta", emptyReceta);

        // Assert: Should be rejected with RECETA_EMPTY
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RECETA_EMPTY", body.GetProperty("code").GetString());
    }
}
