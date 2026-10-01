using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Products;

/// <summary>
/// Tests for product type creation (Venta Directa, Insumo, Ensamblado with recipe),
/// audit log generation on create/update, and Insumo exclusion from product list.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Products")]
public class ProductTypesTests : IntegrationTestBase
{
    public ProductTypesTests(PostgresFixture fixture) : base(fixture) { }

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

    // ── 10.1: Product Type Creation Tests ─────────────────────────────────────

    [DockerAvailableFact]
    public async Task Create_VentaDirecta_Returns201()
    {
        // Arrange
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new CreateProductoRequest(
            Nombre: "Coca Cola 500ml",
            TipoArticulo: "Venta Directa",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Unidades",
            CostoProduccion: 0.80m,
            PrecioLista: 1.50m,
            PrecioMinimo: 1.20m);

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/productos", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ProductoDto>();
        Assert.NotNull(dto);
        Assert.Equal("Coca Cola 500ml", dto.Nombre);
        Assert.Equal("Venta Directa", dto.TipoArticulo);
        Assert.True(dto.ManejaStock);
        Assert.Equal(1.50m, dto.PrecioLista);
        Assert.Equal(1.20m, dto.PrecioMinimo);
        Assert.Equal(0.80m, dto.CostoProduccion);
    }

    [DockerAvailableFact]
    public async Task Create_Insumo_Returns201()
    {
        // Arrange
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new CreateProductoRequest(
            Nombre: "Harina de Trigo 1kg",
            TipoArticulo: "Insumo",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Kilogramos",
            CostoProduccion: 1.20m,
            PrecioLista: 0m,
            PrecioMinimo: 0m);

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/productos", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ProductoDto>();
        Assert.NotNull(dto);
        Assert.Equal("Harina de Trigo 1kg", dto.Nombre);
        Assert.Equal("Insumo", dto.TipoArticulo);
    }

    [DockerAvailableFact]
    public async Task Create_Ensamblado_WithReceta_Returns200()
    {
        // Arrange: Create a Venta Directa product first (as the ingredient)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Step 1: Create ingredient product
        var ingredientRequest = new CreateProductoRequest(
            Nombre: "Harina Ingrediente",
            TipoArticulo: "Venta Directa",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Kilogramos",
            CostoProduccion: 1.00m,
            PrecioLista: 2.00m,
            PrecioMinimo: 1.50m);

        var ingredientResponse = await Client.PostAsJsonAsync("/api/tenants/productos", ingredientRequest);
        Assert.Equal(HttpStatusCode.Created, ingredientResponse.StatusCode);
        var ingredientDto = await ingredientResponse.Content.ReadFromJsonAsync<ProductoDto>();

        // Step 2: Create the product that will become Ensamblado (initially as Venta Directa)
        var productoRequest = new CreateProductoRequest(
            Nombre: "Pan Integral",
            TipoArticulo: "Venta Directa",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Unidades",
            CostoProduccion: 0.50m,
            PrecioLista: 1.50m,
            PrecioMinimo: 1.00m);

        var productoResponse = await Client.PostAsJsonAsync("/api/tenants/productos", productoRequest);
        Assert.Equal(HttpStatusCode.Created, productoResponse.StatusCode);
        var productoDto = await productoResponse.Content.ReadFromJsonAsync<ProductoDto>();

        // Step 3: Set recipe on the product → makes it Ensamblado
        var recetaRequest = new[] { new { IngredienteId = ingredientDto!.Id, CantidadRequerida = 0.5m } };
        var recetaResponse = await Client.PostAsJsonAsync($"/api/tenants/productos/{productoDto!.Id}/receta", recetaRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, recetaResponse.StatusCode);

        // Verify the product type was changed to Ensamblado in DB
        await using var verifyDb = CreateDbContext();
        var updatedProducto = await verifyDb.Productos
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == productoDto.Id);
        Assert.Equal("Ensamblado", updatedProducto.TipoArticulo);
    }

    // ── 10.3: Audit Log Tests ─────────────────────────────────────────────────

    [DockerAvailableFact]
    public async Task Create_Product_GeneratesAuditLog()
    {
        // Arrange
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new CreateProductoRequest(
            Nombre: "Producto Auditado",
            TipoArticulo: "Venta Directa",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Unidades",
            CostoProduccion: 2.00m,
            PrecioLista: 5.00m,
            PrecioMinimo: 3.50m);

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/productos", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ProductoDto>();

        // Assert: Verify audit log was created
        await using var verifyDb = CreateDbContext();
        var auditLog = await verifyDb.LogsAuditoria
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l =>
                l.ComercioId == comercio.Id &&
                l.Accion == "Crear" &&
                l.TablaAfectada == "Productos" &&
                l.RegistroId == dto!.Id.ToString());

        Assert.NotNull(auditLog);
        Assert.Equal(gerente.Id, auditLog.UsuarioId);
        Assert.NotNull(auditLog.ValoresNuevos);
    }

    [DockerAvailableFact]
    public async Task Update_Product_GeneratesAuditLog()
    {
        // Arrange: Create a product first
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var createRequest = new CreateProductoRequest(
            Nombre: "Producto Original",
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
        var createdDto = await createResponse.Content.ReadFromJsonAsync<ProductoDto>();

        // Act: Update the product
        var updateRequest = new UpdateProductoRequest(
            Nombre: "Producto Actualizado",
            TipoArticulo: "Venta Directa",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Unidades",
            CostoProduccion: 1.50m,
            PrecioLista: 4.00m,
            PrecioMinimo: 2.50m);

        var updateResponse = await Client.PutAsJsonAsync($"/api/tenants/productos/{createdDto!.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Assert: Verify audit log for update
        await using var verifyDb = CreateDbContext();
        var auditLog = await verifyDb.LogsAuditoria
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l =>
                l.ComercioId == comercio.Id &&
                l.Accion == "Actualizar" &&
                l.TablaAfectada == "Productos" &&
                l.RegistroId == createdDto.Id.ToString());

        Assert.NotNull(auditLog);
        Assert.Equal(gerente.Id, auditLog.UsuarioId);
        Assert.NotNull(auditLog.ValoresAnteriores);
        Assert.NotNull(auditLog.ValoresNuevos);
    }

    // ── 10.4: Insumo Exclusion Tests ──────────────────────────────────────────

    [DockerAvailableFact]
    public async Task Insumo_ExcludedFromProductList()
    {
        // Arrange: Create products of all types
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Create products directly in DB
        var ventaDirecta = await TestDataBuilder.Producto()
            .EnComercio(comercio.Id).ConTipo("Venta Directa").ConNombre("Producto VD").CrearAsync(db);
        var insumo = await TestDataBuilder.Producto()
            .EnComercio(comercio.Id).ConTipo("Insumo").ConNombre("Producto Insumo").CrearAsync(db);
        var ensamblado = await TestDataBuilder.Producto()
            .EnComercio(comercio.Id).ConTipo("Ensamblado").ConNombre("Producto Ensamblado").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Act: GET product list
        var response = await Client.GetAsync("/api/tenants/productos");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var productos = await response.Content.ReadFromJsonAsync<List<ProductoDto>>();
        Assert.NotNull(productos);

        // Insumo should NOT appear in the list
        Assert.DoesNotContain(productos, p => p.TipoArticulo == "Insumo");
        Assert.DoesNotContain(productos, p => p.Nombre == "Producto Insumo");

        // Venta Directa and Ensamblado should appear
        Assert.Contains(productos, p => p.Nombre == "Producto VD");
        Assert.Contains(productos, p => p.Nombre == "Producto Ensamblado");
    }

    [DockerAvailableFact]
    public async Task Insumo_ExcludedFromSaleEndpoint()
    {
        // Arrange: Create commerce with Insumo product
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);

        // Add ConfiguracionComercio (required for sales)
        db.Set<ConfiguracionComercio>().Add(new ConfiguracionComercio
        {
            ComercioId = comercio.Id,
            EsBarEscolar = false,
            MostrarBotonCliente = false,
            PermiteVentaEnNegativo = false,
            ImpresionAutomaticaTicket = false
        });

        // Seed ConfiguracionesComprobante para que la validación de TipoComprobante pase
        db.Set<ConfiguracionComprobante>().Add(new ConfiguracionComprobante
        {
            ComercioId = comercio.Id,
            TipoComprobante = "Ticket Digital",
            Habilitado = true,
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var cajero = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);
        var insumo = await TestDataBuilder.Producto()
            .EnComercio(comercio.Id).ConTipo("Insumo").ConNombre("Ingrediente No Vendible").CrearAsync(db);

        AuthenticateAs(cajero.Id, comercio.Id, "Cajero");

        // Act: Try to sell the Insumo product
        var ventaRequest = new CrearVentaRequest(
            SucursalId: sucursal.Id,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: insumo.Id, Cantidad: 1)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", ventaRequest);

        // Assert: Should be rejected with INSUMO_NOT_SELLABLE
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INSUMO_NOT_SELLABLE", body.GetProperty("code").GetString());
    }
}
