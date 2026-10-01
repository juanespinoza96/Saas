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
/// Property-based tests for Ensamblado BOM-based stock decrement invariant.
/// **Validates: Requirements 7.3, 10.4**
/// </summary>
public class Property9_EnsambladoBOMStockDecrementTests
{
    private const int ComercioId = 1;
    private const int SucursalId = 1;
    private const decimal InitialStock = 10000m;

    private static (AppDbContext db, StockService service) CreateServiceWithContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns(ComercioId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var dbContext = new AppDbContext(options, tenantContextMock.Object);

        // Seed base entities
        dbContext.Planes.Add(new Plan
        {
            Id = 1,
            Nombre = "Básico",
            Precio = 10m,
            LimiteUsuarios = 5,
            LimiteAtributos = 10
        });

        dbContext.Comercios.Add(new Comercio
        {
            Id = ComercioId,
            Ruc = "1234567890001",
            RazonSocial = "Test Comercio",
            PlanId = 1,
            FechaRegistro = DateTime.UtcNow
        });

        dbContext.ConfiguracionesComercio.Add(new ConfiguracionComercio
        {
            ComercioId = ComercioId,
            PermiteVentaEnNegativo = true,
            EsBarEscolar = false,
            MostrarBotonCliente = false,
            ImpresionAutomaticaTicket = false
        });

        dbContext.Sucursales.Add(new Sucursal
        {
            Id = SucursalId,
            ComercioId = ComercioId,
            Nombre = "Sucursal Principal"
        });

        dbContext.SaveChanges();

        var notificationServiceMock = new Mock<INotificationService>();
        var loggerMock = new Mock<ILogger<StockService>>();
        var auditServiceMock = new Mock<IAuditService>();

        var service = new StockService(dbContext, notificationServiceMock.Object, auditServiceMock.Object, loggerMock.Object);

        return (dbContext, service);
    }

    /// <summary>
    /// Property 9: For any Ensamblado product with a set of recipe ingredients,
    /// after calling DecrementarStockEnsambladoAsync with quantity Q:
    /// 1. Each ingredient's CantidadFisica is decreased by exactly (CantidadRequerida × Q)
    /// 2. The Ensamblado product's own stock (if any) is NOT affected.
    /// **Validates: Requirements 7.3, 10.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool EnsambladoDecrement_EachIngredientDecreasedByRecipeTimesQuantity(
        PositiveInt cantidadVendidaRaw, PositiveInt numIngredientesRaw)
    {
        // Constrain inputs to reasonable ranges
        var cantidadVendida = (decimal)((cantidadVendidaRaw.Get % 20) + 1); // 1–20
        var numIngredientes = (numIngredientesRaw.Get % 5) + 1; // 1–5

        var (db, service) = CreateServiceWithContext();
        using (db)
        {
            // Create Ensamblado product
            var ensamblado = new Producto
            {
                ComercioId = ComercioId,
                Nombre = "Producto Ensamblado",
                TipoArticulo = "Ensamblado",
                ManejaStock = true,
                PrecioLista = 50m,
                PrecioMinimo = 40m,
                CostoProduccion = 30m
            };
            db.Productos.Add(ensamblado);
            db.SaveChanges();

            // Seed stock for the ensamblado itself (to verify it's NOT decremented)
            var ensambladoStock = new StockSucursal
            {
                ProductoId = ensamblado.Id,
                SucursalId = SucursalId,
                CantidadFisica = InitialStock,
                StockMinimo = 0m
            };
            db.StockSucursal.Add(ensambladoStock);
            db.SaveChanges();

            // Create ingredients with recipes and stock
            var ingredientData = new List<(int IngredienteId, decimal CantidadRequerida)>();

            for (int i = 0; i < numIngredientes; i++)
            {
                var ingrediente = new Producto
                {
                    ComercioId = ComercioId,
                    Nombre = $"Insumo {i + 1}",
                    TipoArticulo = "Insumo",
                    ManejaStock = true,
                    PrecioLista = 5m,
                    PrecioMinimo = 3m,
                    CostoProduccion = 2m
                };
                db.Productos.Add(ingrediente);
                db.SaveChanges();

                // Use a deterministic CantidadRequerida based on index (1–10)
                var cantidadRequerida = (decimal)((i % 10) + 1);

                db.RecetasProducto.Add(new RecetaProducto
                {
                    ProductoFinalId = ensamblado.Id,
                    IngredienteId = ingrediente.Id,
                    CantidadRequerida = cantidadRequerida
                });

                db.StockSucursal.Add(new StockSucursal
                {
                    ProductoId = ingrediente.Id,
                    SucursalId = SucursalId,
                    CantidadFisica = InitialStock,
                    StockMinimo = 0m
                });

                db.SaveChanges();

                ingredientData.Add((ingrediente.Id, cantidadRequerida));
            }

            // Act: call DecrementarStockEnsambladoAsync
            var result = service.DecrementarStockEnsambladoAsync(
                ensamblado.Id, SucursalId, cantidadVendida, ComercioId)
                .GetAwaiter().GetResult();

            if (!result.Success)
                return false;

            // Verify: each ingredient's stock decreased by exactly (CantidadRequerida * Q)
            foreach (var (ingredienteId, cantidadRequerida) in ingredientData)
            {
                var stockActual = db.StockSucursal
                    .IgnoreQueryFilters()
                    .First(s => s.ProductoId == ingredienteId && s.SucursalId == SucursalId);

                var expectedStock = InitialStock - (cantidadRequerida * cantidadVendida);
                if (stockActual.CantidadFisica != expectedStock)
                    return false;
            }

            // Verify: ensamblado's own stock is NOT affected
            var ensambladoStockAfter = db.StockSucursal
                .IgnoreQueryFilters()
                .First(s => s.ProductoId == ensamblado.Id && s.SucursalId == SucursalId);

            if (ensambladoStockAfter.CantidadFisica != InitialStock)
                return false;

            return true;
        }
    }
}
