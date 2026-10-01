using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Domain.Entities;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Integration tests for the complete sale flow:
/// Controller → Service → Repository → PostgreSQL DB.
/// Validates stock verification and audit trail creation.
/// </summary>
[Collection("Postgres")]
public class SaleFlowIntegrationTests : IntegrationTestBase
{
    public SaleFlowIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task CrearVenta_ValidSale_CreatesVentaDecrementsStockAndAudits()
    {
        // Arrange: Seed complete commerce hierarchy
        var (comercioId, sucursalId, usuarioId, productoId) = await SeedCommerceWithProduct(stock: 10);
        AuthenticateAs(usuarioId, comercioId, "Cajero");

        var request = new CrearVentaRequest(
            SucursalId: sucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: productoId, Cantidad: 3)
            });

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert: Sale created successfully
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var ventaResponse = await response.Content.ReadFromJsonAsync<VentaResponse>();
        Assert.NotNull(ventaResponse);
        Assert.Equal(comercioId, ventaResponse.ComercioId);
        Assert.Equal(sucursalId, ventaResponse.SucursalId);
        Assert.Single(ventaResponse.Detalles);
        Assert.Equal(3m, ventaResponse.Detalles[0].Cantidad);

        // Assert: Stock decremented in DB
        await using var verifyContext = CreateDbContext();
        var stockRecord = await verifyContext.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == productoId && s.SucursalId == sucursalId);
        Assert.Equal(7m, stockRecord.CantidadFisica); // 10 - 3 = 7

        // Assert: Audit log created
        var auditLog = await verifyContext.LogsAuditoria
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.ComercioId == comercioId && l.Accion == "Crear" && l.TablaAfectada == "Ventas");
        Assert.NotNull(auditLog);
        Assert.Equal(usuarioId, auditLog.UsuarioId);
    }

    [DockerAvailableFact]
    public async Task CrearVenta_InsufficientStock_Returns409WhenNegativeNotAllowed()
    {
        // Arrange: Seed with only 2 units in stock, PermiteVentaEnNegativo = false
        var (comercioId, sucursalId, usuarioId, productoId) = await SeedCommerceWithProduct(stock: 2);
        AuthenticateAs(usuarioId, comercioId, "Cajero");

        var request = new CrearVentaRequest(
            SucursalId: sucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: productoId, Cantidad: 5) // Trying to sell 5 with only 2 in stock
            });

        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert: Conflict because stock is insufficient
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Verify stock was NOT decremented
        await using var verifyContext = CreateDbContext();
        var stockRecord = await verifyContext.StockSucursal
            .IgnoreQueryFilters()
            .FirstAsync(s => s.ProductoId == productoId && s.SucursalId == sucursalId);
        Assert.Equal(2m, stockRecord.CantidadFisica); // Unchanged
    }

    /// <summary>
    /// Seeds a full commerce hierarchy with a product and stock.
    /// Returns (comercioId, sucursalId, usuarioId, productoId).
    /// </summary>
    private async Task<(int comercioId, int sucursalId, int usuarioId, int productoId)> SeedCommerceWithProduct(decimal stock)
    {
        await using var context = CreateDbContext();

        var plan = new Plan
        {
            Nombre = "Plan Test",
            Precio = 30m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = $"TEST{Guid.NewGuid():N}"[..13],
            RazonSocial = "Comercio Test Sale",
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
            Nombre = "Sucursal Central",
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
            Nombre = "Cajero Test",
            Email = $"cajero-{Guid.NewGuid():N}@test.com",
            PasswordHash = "$2a$12$dummyhashforintegrationtests1234567890123456789",
            Rol = "Cajero",
            Activo = true
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        var producto = new Producto
        {
            ComercioId = comercio.Id,
            Nombre = "Producto Test",
            TipoArticulo = "Venta Directa",
            ManejaStock = true,
            PrecioLista = 5.50m,
            PrecioMinimo = 4.00m,
            CostoProduccion = 3.00m
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        var stockSucursal = new StockSucursal
        {
            ProductoId = producto.Id,
            SucursalId = sucursal.Id,
            CantidadFisica = stock,
            StockMinimo = 1
        };
        context.StockSucursal.Add(stockSucursal);
        await context.SaveChangesAsync();

        return (comercio.Id, sucursal.Id, usuario.Id, producto.Id);
    }
}
