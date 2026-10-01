using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Domain.Entities;

namespace SaasPOS.Tests.Integration.Stock;

/// <summary>
/// Integration tests verifying that ConfiguracionComercio.PermiteVentaEnNegativo
/// correctly blocks or allows sales when stock would go negative.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Stock")]
public class NegativeStockPolicyTests : IntegrationTestBase
{
    public NegativeStockPolicyTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task PermiteVentaEnNegativo_False_BlocksVentaDirecta_WhenStockInsufficient()
    {
        // Arrange: Stock=5, PermiteVentaEnNegativo=false
        var (comercioId, sucursalId, usuarioId, productoId) =
            await SeedVentaDirectaCommerce(initialStock: 5m, permiteNegativo: false);
        AuthenticateAs(usuarioId, comercioId, "Cajero");

        // Act: Try to sell 10 units (would need 10 but only 5 available)
        var request = new CrearVentaRequest(
            SucursalId: sucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: productoId, Cantidad: 10)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert: Should be blocked with 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Verify stock unchanged at 5
        await using var verifyDb = CreateDbContext();
        var stock = await verifyDb.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == productoId && s.SucursalId == sucursalId);
        Assert.Equal(5m, stock.CantidadFisica);
    }

    [DockerAvailableFact]
    public async Task PermiteVentaEnNegativo_False_BlocksEnsamblado_WhenIngredientInsufficient()
    {
        // Arrange: Ingredient stock=2, recipe requires 0.5 per unit, sell 5 → needs 2.5
        var seed = await SeedEnsambladoCommerce(
            ingredientStock: 2m, permiteNegativo: false);
        AuthenticateAs(seed.UsuarioId, seed.ComercioId, "Cajero");

        // Act: Sell 5 units of Ensamblado (requires 0.5*5=2.5, but only 2 available)
        var request = new CrearVentaRequest(
            SucursalId: seed.SucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: seed.EnsambladoId, Cantidad: 5)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert: Should be blocked with 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Verify ingredient stock unchanged at 2
        await using var verifyDb = CreateDbContext();
        var stock = await verifyDb.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == seed.IngredientId && s.SucursalId == seed.SucursalId);
        Assert.Equal(2m, stock.CantidadFisica);
    }

    [DockerAvailableFact]
    public async Task PermiteVentaEnNegativo_True_AllowsSale_WithNegativeStock()
    {
        // Arrange: Stock=5, PermiteVentaEnNegativo=true
        var (comercioId, sucursalId, usuarioId, productoId) =
            await SeedVentaDirectaCommerce(initialStock: 5m, permiteNegativo: true);
        AuthenticateAs(usuarioId, comercioId, "Cajero");

        // Act: Sell 10 units (stock will go to -5)
        var request = new CrearVentaRequest(
            SucursalId: sucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: productoId, Cantidad: 10)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert: Sale should succeed with 201 Created
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Verify stock is now -5
        await using var verifyDb = CreateDbContext();
        var stock = await verifyDb.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == productoId && s.SucursalId == sucursalId);
        Assert.Equal(-5m, stock.CantidadFisica);
    }

    #region Seed Helpers

    private async Task<(int comercioId, int sucursalId, int usuarioId, int productoId)>
        SeedVentaDirectaCommerce(decimal initialStock, bool permiteNegativo)
    {
        await using var context = CreateDbContext();

        var plan = new Plan
        {
            Nombre = "Plan Test NegStock",
            Precio = 30m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = $"TEST{Guid.NewGuid():N}"[..13],
            RazonSocial = "Comercio NegStock Test",
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
            PermiteVentaEnNegativo = permiteNegativo,
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
            Nombre = "Sucursal NegStock Test",
            Direccion = "Calle NegStock 123",
            Telefono = "0991234567",
            SerieFacturacion = "001-001"
        };
        context.Sucursales.Add(sucursal);
        await context.SaveChangesAsync();

        var usuario = new Usuario
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            Nombre = "Cajero NegStock",
            Email = $"cajero-negstock-{Guid.NewGuid():N}@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            Rol = "Cajero",
            Activo = true
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        var producto = new Producto
        {
            ComercioId = comercio.Id,
            Nombre = "Producto VD NegStock",
            TipoArticulo = "Venta Directa",
            ManejaStock = true,
            PrecioLista = 10m,
            PrecioMinimo = 8m,
            CostoProduccion = 5m
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        var stock = new StockSucursal
        {
            ProductoId = producto.Id,
            SucursalId = sucursal.Id,
            CantidadFisica = initialStock,
            StockMinimo = 0m
        };
        context.StockSucursal.Add(stock);
        await context.SaveChangesAsync();

        return (comercio.Id, sucursal.Id, usuario.Id, producto.Id);
    }

    private async Task<EnsambladoNegSeedResult> SeedEnsambladoCommerce(
        decimal ingredientStock, bool permiteNegativo)
    {
        await using var context = CreateDbContext();

        var plan = new Plan
        {
            Nombre = "Plan Test NegBOM",
            Precio = 30m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = $"TEST{Guid.NewGuid():N}"[..13],
            RazonSocial = "Comercio NegBOM Test",
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
            PermiteVentaEnNegativo = permiteNegativo,
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
            Nombre = "Sucursal NegBOM Test",
            Direccion = "Calle NegBOM 456",
            Telefono = "0997654321",
            SerieFacturacion = "001-001"
        };
        context.Sucursales.Add(sucursal);
        await context.SaveChangesAsync();

        var usuario = new Usuario
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            Nombre = "Cajero NegBOM",
            Email = $"cajero-negbom-{Guid.NewGuid():N}@test.com",
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
            Nombre = "Producto Ensamblado NegBOM",
            TipoArticulo = "Ensamblado",
            ManejaStock = true,
            PrecioLista = 25m,
            PrecioMinimo = 20m,
            CostoProduccion = 12m
        };
        context.Productos.Add(ensamblado);
        await context.SaveChangesAsync();

        // Ingredient (Insumo)
        var ingredient = new Producto
        {
            ComercioId = comercio.Id,
            Nombre = "Ingrediente NegBOM",
            TipoArticulo = "Insumo",
            ManejaStock = true,
            PrecioLista = 3m,
            PrecioMinimo = 2m,
            CostoProduccion = 1m
        };
        context.Productos.Add(ingredient);
        await context.SaveChangesAsync();

        // Recipe: 0.5 units of ingredient per ensamblado
        var receta = new RecetaProducto
        {
            ProductoFinalId = ensamblado.Id,
            IngredienteId = ingredient.Id,
            CantidadRequerida = 0.5m
        };
        context.RecetasProducto.Add(receta);
        await context.SaveChangesAsync();

        // Stock for ingredient
        var stock = new StockSucursal
        {
            ProductoId = ingredient.Id,
            SucursalId = sucursal.Id,
            CantidadFisica = ingredientStock,
            StockMinimo = 0m
        };
        context.StockSucursal.Add(stock);

        // Stock for the ensamblado itself
        var stockEnsamblado = new StockSucursal
        {
            ProductoId = ensamblado.Id,
            SucursalId = sucursal.Id,
            CantidadFisica = 50m,
            StockMinimo = 0m
        };
        context.StockSucursal.Add(stockEnsamblado);
        await context.SaveChangesAsync();

        return new EnsambladoNegSeedResult(
            ComercioId: comercio.Id,
            SucursalId: sucursal.Id,
            UsuarioId: usuario.Id,
            EnsambladoId: ensamblado.Id,
            IngredientId: ingredient.Id);
    }

    private record EnsambladoNegSeedResult(
        int ComercioId,
        int SucursalId,
        int UsuarioId,
        int EnsambladoId,
        int IngredientId);

    #endregion
}
