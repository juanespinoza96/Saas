using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Domain.Entities;

namespace SaasPOS.Tests.Integration.Stock;

/// <summary>
/// Integration tests verifying that Ensamblado (BOM-based) sales correctly
/// decrement ingredient stock per RecetaProducto, and do NOT change
/// the ensamblado product's own stock.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Stock")]
public class BomDecrementTests : IntegrationTestBase
{
    public BomDecrementTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task Ensamblado_Sale_DecrementsIngredients_PerBOM()
    {
        // Arrange: Seed ensamblado with 2 ingredients
        // Ingredient A: CantidadRequerida=0.5, stock=100
        // Ingredient B: CantidadRequerida=0.2, stock=100
        var seed = await SeedEnsambladoCommerce(
            ingredientAStock: 100m, ingredientBStock: 100m, ensambladoStock: 50m);
        AuthenticateAs(seed.UsuarioId, seed.ComercioId, "Cajero");

        // Act: Sell 3 units of the Ensamblado
        var request = new CrearVentaRequest(
            SucursalId: seed.SucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: seed.EnsambladoId, Cantidad: 3)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verifyDb = CreateDbContext();

        // Ingredient A: 100 - (0.5 × 3) = 98.5
        var stockA = await verifyDb.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == seed.IngredientAId && s.SucursalId == seed.SucursalId);
        Assert.Equal(98.5m, stockA.CantidadFisica);

        // Ingredient B: 100 - (0.2 × 3) = 99.4
        var stockB = await verifyDb.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == seed.IngredientBId && s.SucursalId == seed.SucursalId);
        Assert.Equal(99.4m, stockB.CantidadFisica);
    }

    [DockerAvailableFact]
    public async Task Ensamblado_Sale_DoesNotChangeOwnStock()
    {
        // Arrange
        var seed = await SeedEnsambladoCommerce(
            ingredientAStock: 100m, ingredientBStock: 100m, ensambladoStock: 50m);
        AuthenticateAs(seed.UsuarioId, seed.ComercioId, "Cajero");

        // Act: Sell 3 units of the Ensamblado
        var request = new CrearVentaRequest(
            SucursalId: seed.SucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: seed.EnsambladoId, Cantidad: 3)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert: Sale succeeded
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Assert: Ensamblado's own stock remains unchanged at 50
        await using var verifyDb = CreateDbContext();
        var ensambladoStock = await verifyDb.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == seed.EnsambladoId && s.SucursalId == seed.SucursalId);
        Assert.Equal(50m, ensambladoStock.CantidadFisica);
    }

    [DockerAvailableFact]
    public async Task Ensamblado_Decrement_IsQuantityTimesCantidadRequerida()
    {
        // Arrange: Same setup but sell a different quantity (5 units)
        var seed = await SeedEnsambladoCommerce(
            ingredientAStock: 100m, ingredientBStock: 100m, ensambladoStock: 50m);
        AuthenticateAs(seed.UsuarioId, seed.ComercioId, "Cajero");

        // Act: Sell 5 units
        var request = new CrearVentaRequest(
            SucursalId: seed.SucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: seed.EnsambladoId, Cantidad: 5)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verifyDb = CreateDbContext();

        // Ingredient A: 100 - (0.5 × 5) = 100 - 2.5 = 97.5
        var stockA = await verifyDb.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == seed.IngredientAId && s.SucursalId == seed.SucursalId);
        Assert.Equal(97.5m, stockA.CantidadFisica);

        // Ingredient B: 100 - (0.2 × 5) = 100 - 1.0 = 99.0
        var stockB = await verifyDb.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == seed.IngredientBId && s.SucursalId == seed.SucursalId);
        Assert.Equal(99.0m, stockB.CantidadFisica);
    }

    /// <summary>
    /// Seeds a full commerce with an Ensamblado product, two ingredients with recipes, and stock records.
    /// </summary>
    private async Task<EnsambladoSeedResult> SeedEnsambladoCommerce(
        decimal ingredientAStock, decimal ingredientBStock, decimal ensambladoStock)
    {
        await using var context = CreateDbContext();

        var plan = new Plan
        {
            Nombre = "Plan Test BOM",
            Precio = 30m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = $"TEST{Guid.NewGuid():N}"[..13],
            RazonSocial = "Comercio BOM Test",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Activo = true,
            FechaRegistro = DateTime.UtcNow
        };
        context.Comercios.Add(comercio);
        await context.SaveChangesAsync();

        var config = new ConfiguracionComercio
        {
            ComercioId = comercio.Id,
            EsBarEscolar = false,
            MostrarBotonCliente = false,
            PermiteVentaEnNegativo = false,
            ImpresionAutomaticaTicket = false
        };
        context.ConfiguracionesComercio.Add(config);

        // Seed ConfiguracionesComprobante para que la validación de TipoComprobante pase
        context.ConfiguracionesComprobante.Add(new ConfiguracionComprobante
        {
            ComercioId = comercio.Id,
            TipoComprobante = "Ticket Digital",
            Habilitado = true,
            FechaCreacion = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var sucursal = new Sucursal
        {
            ComercioId = comercio.Id,
            Nombre = "Sucursal BOM Test",
            Direccion = "Calle BOM 456",
            Telefono = "0997654321",
            SerieFacturacion = "001-001"
        };
        context.Sucursales.Add(sucursal);
        await context.SaveChangesAsync();

        var usuario = new Usuario
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            Nombre = "Cajero BOM",
            Email = $"cajero-bom-{Guid.NewGuid():N}@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            Rol = "Cajero",
            Activo = true
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        // Ensamblado product
        var ensamblado = new Producto
        {
            ComercioId = comercio.Id,
            Nombre = "Producto Ensamblado",
            TipoArticulo = "Ensamblado",
            ManejaStock = true,
            PrecioLista = 25m,
            PrecioMinimo = 20m,
            CostoProduccion = 12m
        };
        context.Productos.Add(ensamblado);
        await context.SaveChangesAsync();

        // Ingredient A (Insumo)
        var ingredientA = new Producto
        {
            ComercioId = comercio.Id,
            Nombre = "Ingrediente A",
            TipoArticulo = "Insumo",
            ManejaStock = true,
            PrecioLista = 3m,
            PrecioMinimo = 2m,
            CostoProduccion = 1m
        };
        context.Productos.Add(ingredientA);
        await context.SaveChangesAsync();

        // Ingredient B (Insumo)
        var ingredientB = new Producto
        {
            ComercioId = comercio.Id,
            Nombre = "Ingrediente B",
            TipoArticulo = "Insumo",
            ManejaStock = true,
            PrecioLista = 2m,
            PrecioMinimo = 1.5m,
            CostoProduccion = 0.8m
        };
        context.Productos.Add(ingredientB);
        await context.SaveChangesAsync();

        // Recipes (BOM)
        var recetaA = new RecetaProducto
        {
            ProductoFinalId = ensamblado.Id,
            IngredienteId = ingredientA.Id,
            CantidadRequerida = 0.5m
        };
        var recetaB = new RecetaProducto
        {
            ProductoFinalId = ensamblado.Id,
            IngredienteId = ingredientB.Id,
            CantidadRequerida = 0.2m
        };
        context.RecetasProducto.Add(recetaA);
        context.RecetasProducto.Add(recetaB);
        await context.SaveChangesAsync();

        // Stock for ingredients
        var stockA = new StockSucursal
        {
            ProductoId = ingredientA.Id,
            SucursalId = sucursal.Id,
            CantidadFisica = ingredientAStock,
            StockMinimo = 5m
        };
        var stockB = new StockSucursal
        {
            ProductoId = ingredientB.Id,
            SucursalId = sucursal.Id,
            CantidadFisica = ingredientBStock,
            StockMinimo = 5m
        };
        context.StockSucursal.Add(stockA);
        context.StockSucursal.Add(stockB);

        // Stock for the ensamblado itself (should NOT change after sale)
        var stockEnsamblado = new StockSucursal
        {
            ProductoId = ensamblado.Id,
            SucursalId = sucursal.Id,
            CantidadFisica = ensambladoStock,
            StockMinimo = 5m
        };
        context.StockSucursal.Add(stockEnsamblado);
        await context.SaveChangesAsync();

        return new EnsambladoSeedResult(
            ComercioId: comercio.Id,
            SucursalId: sucursal.Id,
            UsuarioId: usuario.Id,
            EnsambladoId: ensamblado.Id,
            IngredientAId: ingredientA.Id,
            IngredientBId: ingredientB.Id);
    }

    private record EnsambladoSeedResult(
        int ComercioId,
        int SucursalId,
        int UsuarioId,
        int EnsambladoId,
        int IngredientAId,
        int IngredientBId);
}
