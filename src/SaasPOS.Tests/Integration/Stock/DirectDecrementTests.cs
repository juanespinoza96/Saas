using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Domain.Entities;

namespace SaasPOS.Tests.Integration.Stock;

/// <summary>
/// Integration tests verifying that Venta Directa sales correctly
/// decrement CantidadFisica in StockSucursal.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Stock")]
public class DirectDecrementTests : IntegrationTestBase
{
    public DirectDecrementTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task VentaDirecta_Sale_DecrementsCantidadFisica()
    {
        // Arrange: Seed full commerce with Venta Directa product, stock=100
        var (comercioId, sucursalId, usuarioId, productoId) = await SeedVentaDirectaCommerce(initialStock: 100m);
        AuthenticateAs(usuarioId, comercioId, "Cajero");

        // Act: Sell 7 units
        var request = new CrearVentaRequest(
            SucursalId: sucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: productoId, Cantidad: 7)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verifyDb = CreateDbContext();
        var updatedStock = await verifyDb.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == productoId && s.SucursalId == sucursalId);
        Assert.Equal(93m, updatedStock.CantidadFisica); // 100 - 7 = 93
    }

    /// <summary>
    /// Seeds a full commerce hierarchy with a Venta Directa product and stock.
    /// </summary>
    private async Task<(int comercioId, int sucursalId, int usuarioId, int productoId)> SeedVentaDirectaCommerce(decimal initialStock)
    {
        await using var context = CreateDbContext();

        var plan = new Plan
        {
            Nombre = "Plan Test Stock",
            Precio = 30m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = $"TEST{Guid.NewGuid():N}"[..13],
            RazonSocial = "Comercio Stock DirectDecrement",
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
            Nombre = "Sucursal Stock Test",
            Direccion = "Calle Principal 123",
            Telefono = "0991234567",
            SerieFacturacion = "001-001"
        };
        context.Sucursales.Add(sucursal);
        await context.SaveChangesAsync();

        var usuario = new Usuario
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            Nombre = "Cajero Stock",
            Email = $"cajero-stock-{Guid.NewGuid():N}@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            Rol = "Cajero",
            Activo = true
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        var producto = new Producto
        {
            ComercioId = comercio.Id,
            Nombre = "Producto VD Stock",
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
            StockMinimo = 5m
        };
        context.StockSucursal.Add(stock);
        await context.SaveChangesAsync();

        return (comercio.Id, sucursal.Id, usuario.Id, producto.Id);
    }
}
