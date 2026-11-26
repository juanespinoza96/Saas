using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Verifica que StockService lee PermiteVentaEnNegativo de ConfiguracionesSucursal (la sucursal de la venta)
/// y que ConfiguracionesComercio.PermiteVentaEnNegativo se ignora.
/// </summary>
public class StockServicePermiteNegativoSucursalTests
{
    private const int SucursalId = 1;
    private const int ProductoId = 100;
    private const int IngredienteId = 200;
    private const int EnsambladoId = 300;

    /// <param name="sucursalPermite">null = sin fila en ConfiguracionesSucursal.</param>
    /// <param name="comercioPermite">Valor de ConfiguracionesComercio (sin uso); null = sin fila.</param>
    private static (AppDbContext db, StockService service) Create(bool? sucursalPermite, bool? comercioPermite)
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
        db.Sucursales.Add(new Sucursal { Id = SucursalId, ComercioId = 1, Nombre = "Sucursal 1" });
        if (sucursalPermite.HasValue)
            db.ConfiguracionesSucursal.Add(new ConfiguracionSucursal { SucursalId = SucursalId, PermiteVentaEnNegativo = sucursalPermite.Value });
        if (comercioPermite.HasValue)
            db.ConfiguracionesComercio.Add(new ConfiguracionComercio { ComercioId = 1, PermiteVentaEnNegativo = comercioPermite.Value });

        // Venta Directa con stock 5
        db.Productos.Add(new Producto { Id = ProductoId, ComercioId = 1, Nombre = "VD", TipoArticulo = "Venta Directa", ManejaStock = true, PrecioLista = 10m, PrecioMinimo = 8m });
        db.StockSucursal.Add(new StockSucursal { ProductoId = ProductoId, SucursalId = SucursalId, CantidadFisica = 5m, StockMinimo = 0m });

        // Ensamblado con un ingrediente (stock 2, receta 0.5 por unidad)
        db.Productos.Add(new Producto { Id = IngredienteId, ComercioId = 1, Nombre = "Ingrediente", TipoArticulo = "Insumo", ManejaStock = true, PrecioLista = 1m, PrecioMinimo = 1m });
        db.Productos.Add(new Producto { Id = EnsambladoId, ComercioId = 1, Nombre = "Ensamblado", TipoArticulo = "Ensamblado", ManejaStock = true, PrecioLista = 20m, PrecioMinimo = 15m });
        db.RecetasProducto.Add(new RecetaProducto { ProductoFinalId = EnsambladoId, IngredienteId = IngredienteId, CantidadRequerida = 0.5m });
        db.StockSucursal.Add(new StockSucursal { ProductoId = IngredienteId, SucursalId = SucursalId, CantidadFisica = 2m, StockMinimo = 0m });
        db.SaveChanges();

        var service = new StockService(
            db,
            new Mock<INotificationService>().Object,
            new Mock<IAuditService>().Object,
            new Mock<ILogger<StockService>>().Object);
        return (db, service);
    }

    [Fact]
    public async Task VentaDirecta_SucursalTrue_PermiteQuedarEnNegativo()
    {
        var (db, service) = Create(sucursalPermite: true, comercioPermite: false);

        var result = await service.DecrementarStockVentaDirectaAsync(ProductoId, SucursalId, 10m, 1);

        Assert.True(result.Success);
        Assert.Equal(-5m, db.StockSucursal.IgnoreQueryFilters().First(s => s.ProductoId == ProductoId).CantidadFisica);
    }

    [Fact]
    public async Task VentaDirecta_SucursalFalse_RechazaConStockInsuficiente()
    {
        var (db, service) = Create(sucursalPermite: false, comercioPermite: true);

        var result = await service.DecrementarStockVentaDirectaAsync(ProductoId, SucursalId, 10m, 1);

        Assert.False(result.Success);
        Assert.Equal("Stock insuficiente", result.Error);
        Assert.Equal(5m, db.StockSucursal.IgnoreQueryFilters().First(s => s.ProductoId == ProductoId).CantidadFisica);
    }

    [Fact]
    public async Task VentaDirecta_SinFilaDeSucursal_SeTrataComoFalse_AunqueComercioDigaTrue()
    {
        var (_, service) = Create(sucursalPermite: null, comercioPermite: true);

        var result = await service.DecrementarStockVentaDirectaAsync(ProductoId, SucursalId, 10m, 1);

        Assert.False(result.Success);
        Assert.Equal("Stock insuficiente", result.Error);
    }

    [Fact]
    public async Task Ensamblado_SucursalTrue_PermiteQuedarEnNegativo()
    {
        var (db, service) = Create(sucursalPermite: true, comercioPermite: false);

        // Necesita 0.5 * 5 = 2.5 y hay 2
        var result = await service.DecrementarStockEnsambladoAsync(EnsambladoId, SucursalId, 5m, 1);

        Assert.True(result.Success);
        Assert.Equal(-0.5m, db.StockSucursal.IgnoreQueryFilters().First(s => s.ProductoId == IngredienteId).CantidadFisica);
    }

    [Fact]
    public async Task Ensamblado_SucursalFalse_RechazaConStockInsuficiente_AunqueComercioDigaTrue()
    {
        var (db, service) = Create(sucursalPermite: false, comercioPermite: true);

        var result = await service.DecrementarStockEnsambladoAsync(EnsambladoId, SucursalId, 5m, 1);

        Assert.False(result.Success);
        Assert.Equal("Stock insuficiente", result.Error);
        Assert.Equal(2m, db.StockSucursal.IgnoreQueryFilters().First(s => s.ProductoId == IngredienteId).CantidadFisica);
    }
}
