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
/// Property-based tests for stock-low notification creation.
/// **Validates: Requirements 7.7, 14.1**
/// </summary>
public class Property17_StockLowNotificationTests
{
    private static (AppDbContext db, StockService service) CreateServiceWithRealNotification()
    {
        var tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.ComercioId).Returns(1);
        tenantMock.Setup(t => t.IsSuperAdmin).Returns(true);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantMock.Object);

        // Seed base entities
        db.Planes.Add(new Plan { Id = 1, Nombre = "Básico", Precio = 10m, LimiteUsuarios = 5, LimiteAtributos = 2 });
        db.Comercios.Add(new Comercio { Id = 1, Ruc = "123", RazonSocial = "Test", PlanId = 1, FechaRegistro = DateTime.UtcNow });
        db.ConfiguracionesComercio.Add(new ConfiguracionComercio { ComercioId = 1, PermiteVentaEnNegativo = true });
        db.Sucursales.Add(new Sucursal { Id = 1, ComercioId = 1, Nombre = "Sucursal 1" });
        db.SaveChanges();

        // Use REAL NotificationService backed by the same InMemory DbContext
        var emailServiceMock = new Mock<IEmailService>();
        var realNotificationService = new NotificationService(db, emailServiceMock.Object);
        var loggerMock = new Mock<ILogger<StockService>>();
        var auditServiceMock = new Mock<IAuditService>();
        var service = new StockService(db, realNotificationService, auditServiceMock.Object, loggerMock.Object);

        return (db, service);
    }

    /// <summary>
    /// Property 17: For any product with StockMinimo > 0, when the Stock_Service processes a sale
    /// that causes CantidadFisica to drop below StockMinimo, a notification record SHALL be created
    /// in Notificaciones with Leida = false, and the notification SHALL be associated with the correct
    /// ComercioId, SucursalId, and ProductoId.
    /// **Validates: Requirements 7.7, 14.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool StockLowEvent_TriggersNotificationCreation(PositiveInt stockMinimoRaw, PositiveInt initialStockOffsetRaw, PositiveInt dropAmountRaw)
    {
        // Constrain values to avoid overflow and ensure meaningful test scenarios
        var stockMinimo = (decimal)(stockMinimoRaw.Get % 1000 + 1);
        // initialStock must be >= stockMinimo so we start above threshold
        var initialStock = stockMinimo + (decimal)(initialStockOffsetRaw.Get % 1000);
        // dropAmount must be large enough to push stock below StockMinimo
        // finalStock = initialStock - dropAmount < stockMinimo
        // dropAmount > initialStock - stockMinimo
        var minDrop = initialStock - stockMinimo + 1;
        var dropAmount = minDrop + (decimal)(dropAmountRaw.Get % 1000);

        var (db, service) = CreateServiceWithRealNotification();
        using (db)
        {
            // Seed product
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

            // Seed stock with initial quantity above threshold
            var stockRecord = new StockSucursal
            {
                ProductoId = 100,
                SucursalId = 1,
                CantidadFisica = initialStock,
                StockMinimo = stockMinimo
            };
            db.StockSucursal.Add(stockRecord);
            db.SaveChanges();

            // Act: decrement stock by amount that drops below StockMinimo
            var result = service.DecrementarStockVentaDirectaAsync(
                productoId: 100,
                sucursalId: 1,
                cantidad: dropAmount,
                comercioId: 1).GetAwaiter().GetResult();

            // The operation should succeed (PermiteVentaEnNegativo = true)
            if (!result.Success)
                return false;

            // Assert: A notification was created
            var notificaciones = db.Notificaciones
                .IgnoreQueryFilters()
                .Where(n => n.ComercioId == 1 && n.ProductoId == 100 && n.SucursalId == 1)
                .ToList();

            if (notificaciones.Count != 1)
                return false;

            var notif = notificaciones[0];

            // Verify Leida = false (Req 14.1)
            if (notif.Leida != false)
                return false;

            // Verify correct associations (Req 14.1)
            if (notif.ComercioId != 1)
                return false;
            if (notif.SucursalId != 1)
                return false;
            if (notif.ProductoId != 100)
                return false;

            // Verify it has a meaningful Titulo and Mensaje (Req 7.7)
            if (string.IsNullOrWhiteSpace(notif.Titulo))
                return false;
            if (string.IsNullOrWhiteSpace(notif.Mensaje))
                return false;

            return true;
        }
    }
}
