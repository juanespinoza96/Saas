using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for Venta Directa stock decrement invariant.
/// **Validates: Requirements 7.2**
/// </summary>
public class Property8_VentaDirectaStockDecrementTests
{
    private static (AppDbContext db, StockService service) CreateServiceWithContext(bool permiteNegativo = true)
    {
        var tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.ComercioId).Returns(1);
        tenantMock.Setup(t => t.IsSuperAdmin).Returns(true);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantMock.Object);

        // Seed comercio with config
        db.Planes.Add(new Plan { Id = 1, Nombre = "Básico", Precio = 10m, LimiteUsuarios = 5, LimiteAtributos = 2 });
        db.Comercios.Add(new Comercio { Id = 1, Ruc = "123", RazonSocial = "Test", PlanId = 1, FechaRegistro = DateTime.UtcNow });
        db.ConfiguracionesComercio.Add(new ConfiguracionComercio { ComercioId = 1, PermiteVentaEnNegativo = permiteNegativo });
        db.Sucursales.Add(new Sucursal { Id = 1, ComercioId = 1, Nombre = "Sucursal 1" });
        db.SaveChanges();

        var notifMock = new Mock<INotificationService>();
        var loggerMock = new Mock<ILogger<StockService>>();
        var auditMock = new Mock<IAuditService>();
        var service = new StockService(db, notifMock.Object, auditMock.Object, loggerMock.Object);

        return (db, service);
    }

    /// <summary>
    /// Property 8: For any Venta Directa product with initial stock S and sale quantity Q,
    /// when PermiteVentaEnNegativo=true, after calling DecrementarStockVentaDirectaAsync
    /// the resulting CantidadFisica SHALL equal S - Q.
    /// **Validates: Requirements 7.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool VentaDirecta_DecrementEqualsExactQuantitySold(PositiveInt initialStockRaw, PositiveInt cantidadRaw)
    {
        var initialStock = (decimal)initialStockRaw.Get;
        var cantidad = (decimal)cantidadRaw.Get;

        var (db, service) = CreateServiceWithContext(permiteNegativo: true);
        using (db)
        {
            // Seed a Venta Directa product with initial stock
            var producto = new Producto
            {
                Id = 100,
                ComercioId = 1,
                Nombre = "Producto Test",
                TipoArticulo = "Venta Directa",
                ManejaStock = true,
                PrecioLista = 10m,
                PrecioMinimo = 5m
            };
            db.Productos.Add(producto);
            db.SaveChanges();

            var stockRecord = new StockSucursal
            {
                ProductoId = 100,
                SucursalId = 1,
                CantidadFisica = initialStock,
                StockMinimo = 0
            };
            db.StockSucursal.Add(stockRecord);
            db.SaveChanges();

            // Act
            var result = service.DecrementarStockVentaDirectaAsync(
                productoId: 100,
                sucursalId: 1,
                cantidad: cantidad,
                comercioId: 1).GetAwaiter().GetResult();

            // With PermiteVentaEnNegativo=true, the operation must always succeed
            if (!result.Success)
                return false;

            // Assert: CantidadFisica == initialStock - cantidad
            var updatedStock = db.StockSucursal
                .IgnoreQueryFilters()
                .First(s => s.ProductoId == 100 && s.SucursalId == 1);

            return updatedStock.CantidadFisica == initialStock - cantidad;
        }
    }
}
