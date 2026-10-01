using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Domain.Entities;

namespace SaasPOS.Tests.Integration.Stock;

/// <summary>
/// Integration tests verifying that selling stock below StockMinimo threshold
/// creates a Notificacion record and enqueues a ColaCorreo to the Gerente.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Stock")]
public class LowStockNotificationTests : IntegrationTestBase
{
    public LowStockNotificationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task StockBelowThreshold_CreatesNotificacion()
    {
        // Arrange: Stock=10, StockMinimo=8, sell 5 → stock goes to 5, which < 8
        var seed = await SeedLowStockCommerce(initialStock: 10m, stockMinimo: 8m);
        AuthenticateAs(seed.UsuarioId, seed.ComercioId, "Cajero");

        // Act: Sell 5 units
        var request = new CrearVentaRequest(
            SucursalId: seed.SucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: seed.ProductoId, Cantidad: 5)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert: Sale succeeded
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Verify a Notificacion record exists with TipoNotificacion="StockBajo"
        await using var verifyDb = CreateDbContext();
        var notificaciones = await verifyDb.Notificaciones
            .IgnoreQueryFilters()
            .Where(n => n.ComercioId == seed.ComercioId && n.TipoNotificacion == "StockBajo")
            .ToListAsync();

        Assert.Single(notificaciones);
        var notificacion = notificaciones[0];
        Assert.Equal("Stock bajo", notificacion.Titulo);
        Assert.False(notificacion.Leida);
        Assert.Contains(seed.ProductoNombre, notificacion.Mensaje);
    }

    [DockerAvailableFact]
    public async Task StockBelowThreshold_CreatesColaCorreo()
    {
        // Arrange: Stock=10, StockMinimo=8, sell 5 → stock goes to 5, which < 8
        var seed = await SeedLowStockCommerce(initialStock: 10m, stockMinimo: 8m);
        AuthenticateAs(seed.UsuarioId, seed.ComercioId, "Cajero");

        // Act: Sell 5 units
        var request = new CrearVentaRequest(
            SucursalId: seed.SucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: seed.ProductoId, Cantidad: 5)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert: Sale succeeded
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Verify a ColaCorreo record exists with Estado="Pendiente" and Asunto contains "Stock bajo"
        await using var verifyDb = CreateDbContext();
        var emails = await verifyDb.ColaCorreos
            .IgnoreQueryFilters()
            .Where(e => e.ComercioId == seed.ComercioId)
            .ToListAsync();

        Assert.Single(emails);
        var email = emails[0];
        Assert.Equal("Pendiente", email.Estado);
        Assert.Contains("Stock bajo", email.Asunto);
        Assert.Equal(seed.GerenteEmail, email.Destinatario);
    }

    [DockerAvailableFact]
    public async Task StockAboveThreshold_NoNotification()
    {
        // Arrange: Stock=100, StockMinimo=5, sell 3 → stock goes to 97, still > 5
        var seed = await SeedLowStockCommerce(initialStock: 100m, stockMinimo: 5m);
        AuthenticateAs(seed.UsuarioId, seed.ComercioId, "Cajero");

        // Act: Sell 3 units (stock=97, which > 5 — no notification expected)
        var request = new CrearVentaRequest(
            SucursalId: seed.SucursalId,
            TipoComprobante: "Ticket Interno",
            ClienteId: null,
            Lineas: new List<LineaVentaRequest>
            {
                new(ProductoId: seed.ProductoId, Cantidad: 3)
            });

        var response = await Client.PostAsJsonAsync("/api/tenants/ventas", request);

        // Assert: Sale succeeded
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Verify NO Notificacion or ColaCorreo record was created
        await using var verifyDb = CreateDbContext();
        var notificaciones = await verifyDb.Notificaciones
            .IgnoreQueryFilters()
            .Where(n => n.ComercioId == seed.ComercioId && n.TipoNotificacion == "StockBajo")
            .ToListAsync();
        Assert.Empty(notificaciones);

        var emails = await verifyDb.ColaCorreos
            .IgnoreQueryFilters()
            .Where(e => e.ComercioId == seed.ComercioId)
            .ToListAsync();
        Assert.Empty(emails);
    }

    #region Seed Helpers

    private async Task<LowStockSeedResult> SeedLowStockCommerce(decimal initialStock, decimal stockMinimo)
    {
        await using var context = CreateDbContext();

        var plan = new Plan
        {
            Nombre = "Plan Test LowStock",
            Precio = 30m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();

        var comercio = new Comercio
        {
            Ruc = $"TEST{Guid.NewGuid():N}"[..13],
            RazonSocial = "Comercio LowStock Test",
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
            Nombre = "Sucursal LowStock Test",
            Direccion = "Calle LowStock 123",
            Telefono = "0991234567",
            SerieFacturacion = "001-001"
        };
        context.Sucursales.Add(sucursal);
        await context.SaveChangesAsync();

        // Cajero user (will perform the sale)
        var usuario = new Usuario
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            Nombre = "Cajero LowStock",
            Email = $"cajero-lowstock-{Guid.NewGuid():N}@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            Rol = "Cajero",
            Activo = true
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        // Gerente user (notification email target)
        var gerenteEmail = $"gerente-lowstock-{Guid.NewGuid():N}@test.com";
        var gerente = new Usuario
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            Nombre = "Gerente LowStock",
            Email = gerenteEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            Rol = "Gerente",
            Activo = true
        };
        context.Usuarios.Add(gerente);
        await context.SaveChangesAsync();

        var productoNombre = "Producto LowStock VD";
        var producto = new Producto
        {
            ComercioId = comercio.Id,
            Nombre = productoNombre,
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
            StockMinimo = stockMinimo
        };
        context.StockSucursal.Add(stock);
        await context.SaveChangesAsync();

        return new LowStockSeedResult(
            ComercioId: comercio.Id,
            SucursalId: sucursal.Id,
            UsuarioId: usuario.Id,
            ProductoId: producto.Id,
            ProductoNombre: productoNombre,
            GerenteEmail: gerenteEmail);
    }

    private record LowStockSeedResult(
        int ComercioId,
        int SucursalId,
        int UsuarioId,
        int ProductoId,
        string ProductoNombre,
        string GerenteEmail);

    #endregion
}
