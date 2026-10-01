using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for negative stock prevention when PermiteVentaEnNegativo is FALSE.
/// **Validates: Requirements 7.4, 7.5**
/// </summary>
public class Property10_NegativeStockPreventionTests
{
    private static (AppDbContext db, StockService service) CreateService(bool permiteNegativo)
    {
        var tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.ComercioId).Returns(1);
        tenantMock.Setup(t => t.IsSuperAdmin).Returns(true);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options, tenantMock.Object);

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
    /// Property A: For any Venta Directa product with stock S and sale quantity Q where Q > S,
    /// when PermiteVentaEnNegativo=FALSE, DecrementarStockVentaDirectaAsync SHALL return
    /// StockResult(false, ...) and stock SHALL remain unchanged.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool VentaDirecta_WhenWouldGoNegative_SaleBlocked(PositiveInt stockRaw, PositiveInt extraRaw)
    {
        var stock = (decimal)stockRaw.Get;
        var extra = (decimal)extraRaw.Get;
        var cantidad = stock + extra; // Always exceeds current stock

        var (db, service) = CreateService(permiteNegativo: false);
        using (db)
        {
            var producto = new Producto
            {
                Id = 100,
                ComercioId = 1,
                Nombre = "Producto VD",
                TipoArticulo = "Venta Directa",
                ManejaStock = true,
                PrecioLista = 10m,
                PrecioMinimo = 5m
            };
            db.Productos.Add(producto);
            db.SaveChanges();

            db.StockSucursal.Add(new StockSucursal
            {
                ProductoId = 100,
                SucursalId = 1,
                CantidadFisica = stock,
                StockMinimo = 0
            });
            db.SaveChanges();

            // Act
            var result = service.DecrementarStockVentaDirectaAsync(
                productoId: 100,
                sucursalId: 1,
                cantidad: cantidad,
                comercioId: 1).GetAwaiter().GetResult();

            // Assert: sale must be blocked
            if (result.Success)
                return false;

            // Assert: stock remains unchanged
            var updatedStock = db.StockSucursal
                .IgnoreQueryFilters()
                .First(s => s.ProductoId == 100 && s.SucursalId == 1);

            return updatedStock.CantidadFisica == stock;
        }
    }

    /// <summary>
    /// Property B: For any Venta Directa product with stock S and sale quantity Q where Q &lt;= S,
    /// when PermiteVentaEnNegativo=FALSE, sale succeeds and stock equals S - Q.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool VentaDirecta_WhenSufficientStock_SaleSucceeds(PositiveInt stockRaw, PositiveInt cantidadRaw)
    {
        // Ensure stock >= cantidad by using stock = stockRaw + cantidadRaw, cantidad = cantidadRaw
        var cantidad = (decimal)cantidadRaw.Get;
        var stock = (decimal)stockRaw.Get + cantidad; // stock always >= cantidad

        var (db, service) = CreateService(permiteNegativo: false);
        using (db)
        {
            var producto = new Producto
            {
                Id = 100,
                ComercioId = 1,
                Nombre = "Producto VD",
                TipoArticulo = "Venta Directa",
                ManejaStock = true,
                PrecioLista = 10m,
                PrecioMinimo = 5m
            };
            db.Productos.Add(producto);
            db.SaveChanges();

            db.StockSucursal.Add(new StockSucursal
            {
                ProductoId = 100,
                SucursalId = 1,
                CantidadFisica = stock,
                StockMinimo = 0
            });
            db.SaveChanges();

            // Act
            var result = service.DecrementarStockVentaDirectaAsync(
                productoId: 100,
                sucursalId: 1,
                cantidad: cantidad,
                comercioId: 1).GetAwaiter().GetResult();

            // Assert: sale must succeed
            if (!result.Success)
                return false;

            // Assert: stock == initial - cantidad
            var updatedStock = db.StockSucursal
                .IgnoreQueryFilters()
                .First(s => s.ProductoId == 100 && s.SucursalId == 1);

            return updatedStock.CantidadFisica == stock - cantidad;
        }
    }

    /// <summary>
    /// Property C: For any Ensamblado product where the sale would cause any ingredient's stock
    /// to go negative, when PermiteVentaEnNegativo=FALSE, DecrementarStockEnsambladoAsync SHALL
    /// return StockResult(false, ...) and ALL ingredient stocks SHALL remain unchanged.
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Ensamblado_WhenAnyIngredientWouldGoNegative_SaleBlocked(PositiveInt cantidadRaw)
    {
        var cantidad = (decimal)cantidadRaw.Get;

        var (db, service) = CreateService(permiteNegativo: false);
        using (db)
        {
            // Product: Ensamblado with id 200
            var ensamblado = new Producto
            {
                Id = 200,
                ComercioId = 1,
                Nombre = "Ensamblado Test",
                TipoArticulo = "Ensamblado",
                ManejaStock = true,
                PrecioLista = 50m,
                PrecioMinimo = 30m
            };
            db.Productos.Add(ensamblado);

            // Ingredient 1: sufficient stock (CantidadRequerida=1, stock=1000)
            var ingrediente1 = new Producto
            {
                Id = 301,
                ComercioId = 1,
                Nombre = "Ingrediente 1",
                TipoArticulo = "Insumo",
                ManejaStock = true,
                PrecioLista = 5m,
                PrecioMinimo = 3m
            };
            db.Productos.Add(ingrediente1);

            // Ingredient 2: insufficient stock (CantidadRequerida=2, stock=1 => needs 2*cantidad but only has 1)
            var ingrediente2 = new Producto
            {
                Id = 302,
                ComercioId = 1,
                Nombre = "Ingrediente 2",
                TipoArticulo = "Insumo",
                ManejaStock = true,
                PrecioLista = 3m,
                PrecioMinimo = 2m
            };
            db.Productos.Add(ingrediente2);
            db.SaveChanges();

            // Recipe entries
            db.RecetasProducto.Add(new RecetaProducto
            {
                ProductoFinalId = 200,
                IngredienteId = 301,
                CantidadRequerida = 1m
            });
            db.RecetasProducto.Add(new RecetaProducto
            {
                ProductoFinalId = 200,
                IngredienteId = 302,
                CantidadRequerida = 2m // Needs 2 * cantidad units
            });
            db.SaveChanges();

            // Stock for ingredient 1: plenty (1000 units)
            decimal stockIngrediente1 = 1000m;
            db.StockSucursal.Add(new StockSucursal
            {
                ProductoId = 301,
                SucursalId = 1,
                CantidadFisica = stockIngrediente1,
                StockMinimo = 0
            });

            // Stock for ingredient 2: only 1 unit (insufficient since needs 2*cantidad and cantidad >= 1)
            decimal stockIngrediente2 = 1m;
            db.StockSucursal.Add(new StockSucursal
            {
                ProductoId = 302,
                SucursalId = 1,
                CantidadFisica = stockIngrediente2,
                StockMinimo = 0
            });
            db.SaveChanges();

            // Act
            var result = service.DecrementarStockEnsambladoAsync(
                productoEnsambladoId: 200,
                sucursalId: 1,
                cantidad: cantidad,
                comercioId: 1).GetAwaiter().GetResult();

            // Assert: sale must be blocked
            if (result.Success)
                return false;

            // Assert: ALL ingredient stocks remain unchanged
            var stock1 = db.StockSucursal
                .IgnoreQueryFilters()
                .First(s => s.ProductoId == 301 && s.SucursalId == 1);

            var stock2 = db.StockSucursal
                .IgnoreQueryFilters()
                .First(s => s.ProductoId == 302 && s.SucursalId == 1);

            return stock1.CantidadFisica == stockIngrediente1
                && stock2.CantidadFisica == stockIngrediente2;
        }
    }
}
