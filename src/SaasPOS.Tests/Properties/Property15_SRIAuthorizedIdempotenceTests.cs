using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for SRI authorized status idempotence.
/// **Validates: Requirements 11.5, 11.7**
///
/// Property 15: SRI authorized status is terminal — cannot regress.
/// "For any sale whose EstadoSRI is already 'Autorizada', calling EmitirFacturaElectronicaAsync
/// SHALL NOT modify the EstadoSRI field, and the service SHALL return immediately indicating
/// success without contacting the SRI."
/// </summary>
public class Property15_SRIAuthorizedIdempotenceTests
{
    private static (AppDbContext db, SRIService service, Mock<HttpMessageHandler> handlerMock) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        var subscriptionGuardMock = new Mock<ISubscriptionGuard>();
        subscriptionGuardMock
            .Setup(s => s.CanEmitirFacturaElectronicaAsync(It.IsAny<int>()))
            .ReturnsAsync(true);

        var notificationServiceMock = new Mock<INotificationService>();
        var loggerMock = new Mock<ILogger<SRIService>>();

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["SRI_ENCRYPTION_KEY"]).Returns("test-encryption-key");
        configMock.Setup(c => c["SRI:UrlRecepcion"]).Returns("https://test.sri.gob.ec/recepcion");
        configMock.Setup(c => c["SRI:UrlAutorizacion"]).Returns("https://test.sri.gob.ec/autorizacion");

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.sri.gob.ec")
        };

        var service = new SRIService(
            db,
            subscriptionGuardMock.Object,
            notificationServiceMock.Object,
            loggerMock.Object,
            configMock.Object,
            httpClient);

        return (db, service, handlerMock);
    }

    private static (Comercio comercio, Venta venta) SeedAuthorizedVenta(
        AppDbContext db, int comercioId, int ventaId)
    {
        // Ensure IDs are positive and distinct
        var safeComercioId = Math.Abs(comercioId % 10000) + 1;
        var safeVentaId = Math.Abs(ventaId % 10000) + 1;

        var plan = new Plan
        {
            Id = 1,
            Nombre = "Intermedio",
            Precio = 29.99m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };

        if (!db.Planes.Any(p => p.Id == 1))
        {
            db.Planes.Add(plan);
            db.SaveChanges();
        }

        var comercio = new Comercio
        {
            Id = safeComercioId,
            Ruc = $"{1000000000000 + safeComercioId}",
            RazonSocial = "Comercio Test",
            PlanId = 1,
            UsaFacturacionSRI = true,
            RutaFirmaElectronica = "/path/to/cert.p12",
            ClaveFirmaEncriptada = "encrypted-key-value",
            Activo = true,
            FechaRegistro = DateTime.UtcNow
        };

        db.Comercios.Add(comercio);
        db.SaveChanges();

        var sucursal = new Sucursal
        {
            Id = safeComercioId,
            ComercioId = safeComercioId,
            Nombre = "Sucursal Principal",
            Direccion = "Dirección Test",
            SerieFacturacion = "001001"
        };

        db.Sucursales.Add(sucursal);
        db.SaveChanges();

        var venta = new Venta
        {
            Id = safeVentaId,
            ComercioId = safeComercioId,
            SucursalId = safeComercioId,
            UsuarioId = 1,
            Total = 100.00m,
            TipoComprobante = "Factura Electronica",
            EstadoSRI = "Autorizada",
            FechaVenta = DateTime.UtcNow
        };

        db.Ventas.Add(venta);
        db.SaveChanges();

        return (comercio, venta);
    }

    // ─── Property 15.1: Authorized status remains unchanged after call ───────

    /// <summary>
    /// For any sale with EstadoSRI = "Autorizada", calling EmitirFacturaElectronicaAsync
    /// does NOT modify the EstadoSRI field — it remains "Autorizada".
    /// **Validates: Requirements 11.5, 11.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AuthorizedStatus_RemainsUnchanged_AfterEmitCall(PositiveInt ventaIdGen, PositiveInt comercioIdGen)
    {
        var (db, service, _) = CreateContext();
        using (db)
        {
            var (comercio, venta) = SeedAuthorizedVenta(db, comercioIdGen.Get, ventaIdGen.Get);

            // Act
            var result = service.EmitirFacturaElectronicaAsync(venta.Id, comercio.Id)
                .GetAwaiter().GetResult();

            // Reload from DB to verify no modification
            var reloadedVenta = db.Ventas.First(v => v.Id == venta.Id);

            return reloadedVenta.EstadoSRI == "Autorizada";
        }
    }

    // ─── Property 15.2: Returns success result with "Autorizada" estado ──────

    /// <summary>
    /// For any sale with EstadoSRI = "Autorizada", calling EmitirFacturaElectronicaAsync
    /// returns SRIResult with Success=true and Estado="Autorizada".
    /// **Validates: Requirements 11.5, 11.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AuthorizedStatus_ReturnsIdempotentSuccess(PositiveInt ventaIdGen, PositiveInt comercioIdGen)
    {
        var (db, service, _) = CreateContext();
        using (db)
        {
            var (comercio, venta) = SeedAuthorizedVenta(db, comercioIdGen.Get, ventaIdGen.Get);

            // Act
            var result = service.EmitirFacturaElectronicaAsync(venta.Id, comercio.Id)
                .GetAwaiter().GetResult();

            // Verify idempotent success response
            return result.Success == true
                && result.Estado == "Autorizada"
                && result.MensajeError == null;
        }
    }

    // ─── Property 15.3: No HTTP call is made for already authorized sales ────

    /// <summary>
    /// For any sale with EstadoSRI = "Autorizada", calling EmitirFacturaElectronicaAsync
    /// does NOT make any HTTP request to SRI (idempotent bypass).
    /// **Validates: Requirements 11.5, 11.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AuthorizedStatus_NoHttpCallMade(PositiveInt ventaIdGen, PositiveInt comercioIdGen)
    {
        var (db, service, handlerMock) = CreateContext();
        using (db)
        {
            var (comercio, venta) = SeedAuthorizedVenta(db, comercioIdGen.Get, ventaIdGen.Get);

            // Act
            var result = service.EmitirFacturaElectronicaAsync(venta.Id, comercio.Id)
                .GetAwaiter().GetResult();

            // Verify no HTTP call was made — the mock handler is Strict,
            // so any call would throw. If we reach here, no call was made.
            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

            return true;
        }
    }

    // ─── Property 15.4: Repeated calls are all idempotent ────────────────────

    /// <summary>
    /// For any sale with EstadoSRI = "Autorizada", calling EmitirFacturaElectronicaAsync
    /// multiple times always returns the same idempotent result and never changes the status.
    /// **Validates: Requirements 11.5, 11.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AuthorizedStatus_RepeatedCalls_AllIdempotent(PositiveInt ventaIdGen, PositiveInt comercioIdGen, PositiveInt callCountGen)
    {
        var callCount = (callCountGen.Get % 5) + 2; // 2 to 6 calls
        var (db, service, _) = CreateContext();
        using (db)
        {
            var (comercio, venta) = SeedAuthorizedVenta(db, comercioIdGen.Get, ventaIdGen.Get);

            for (int i = 0; i < callCount; i++)
            {
                var result = service.EmitirFacturaElectronicaAsync(venta.Id, comercio.Id)
                    .GetAwaiter().GetResult();

                if (!result.Success || result.Estado != "Autorizada")
                    return false;
            }

            // Verify DB state is still "Autorizada" after all calls
            var reloadedVenta = db.Ventas.First(v => v.Id == venta.Id);
            return reloadedVenta.EstadoSRI == "Autorizada";
        }
    }
}
