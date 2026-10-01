using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SaasPOS.Api.Controllers.Tenants;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using System.Security.Claims;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for volume price special price floor validation.
/// **Validates: Requirements 8.4**
/// </summary>
public class Property12_SpecialPriceFloorTests
{
    private static (AppDbContext db, ProductosController controller) CreateControllerWithContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(false);
        tenantContextMock.Setup(t => t.ComercioId).Returns(1);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new AppDbContext(options, tenantContextMock.Object);

        // Seed a comercio and plan so FK constraints are satisfied
        dbContext.Comercios.Add(new Comercio
        {
            Id = 1,
            Ruc = "1234567890001",
            RazonSocial = "Test Comercio",
            PlanId = 1,
            FechaRegistro = DateTime.UtcNow
        });
        dbContext.Planes.Add(new Plan
        {
            Id = 1,
            Nombre = "Básico",
            Precio = 10m,
            LimiteUsuarios = 5,
            LimiteAtributos = 2
        });
        dbContext.SaveChanges();

        var auditServiceMock = new Mock<IAuditService>();

        var controller = new ProductosController(dbContext, tenantContextMock.Object, auditServiceMock.Object);

        // Set up ControllerContext with claims (sub = "1")
        var claims = new List<Claim> { new Claim("sub", "1") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        return (dbContext, controller);
    }

    /// <summary>
    /// Property 12: For any product with PrecioMinimo > 0 and any PrecioEspecial below PrecioMinimo,
    /// the CreatePrecioVolumen endpoint must always reject with HTTP 422.
    /// **Validates: Requirements 8.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PrecioEspecialBelowMinimo_AlwaysRejectedWith422(PositiveInt precioMinRaw, PositiveInt offsetRaw)
    {
        var (db, controller) = CreateControllerWithContext();
        using (db)
        {
            var precioMinimo = (decimal)precioMinRaw.Get;
            var offset = (decimal)offsetRaw.Get; // offset is always >= 1
            var precioEspecial = precioMinimo - offset; // always < precioMinimo

            // Seed a product with the generated PrecioMinimo
            var producto = new Producto
            {
                ComercioId = 1,
                Nombre = "Producto Test",
                TipoArticulo = "Venta Directa",
                ManejaStock = true,
                CostoProduccion = 0m,
                PrecioLista = precioMinimo + 10m,
                PrecioMinimo = precioMinimo
            };
            db.Productos.Add(producto);
            db.SaveChanges();

            // Attempt to create a volume price rule with PrecioEspecial < PrecioMinimo
            var request = new CreatePrecioVolumenRequest(
                CantidadMinima: 1m,
                PrecioEspecial: precioEspecial);

            var result = controller.CreatePrecioVolumen(producto.Id, request).GetAwaiter().GetResult();

            // If PrecioEspecial <= 0, the controller rejects with 400 (INVALID_PRECIO_ESPECIAL)
            // If PrecioEspecial > 0 but < PrecioMinimo, the controller rejects with 422 (PRECIO_BELOW_MINIMUM)
            // In both cases, the request is rejected (not accepted), which validates the requirement.
            if (precioEspecial <= 0)
            {
                // Controller validates PrecioEspecial > 0 first, returns 400
                return result is BadRequestObjectResult;
            }

            // PrecioEspecial > 0 but < PrecioMinimo → should return 422
            if (result is not UnprocessableEntityObjectResult unprocessable)
                return false;

            return unprocessable.StatusCode == 422;
        }
    }

    /// <summary>
    /// Property 12: For any product with PrecioMinimo > 0 and any PrecioEspecial >= PrecioMinimo,
    /// the CreatePrecioVolumen endpoint must always accept and return HTTP 201.
    /// **Validates: Requirements 8.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PrecioEspecialAtOrAboveMinimo_AlwaysAccepted(PositiveInt precioMinRaw, NonNegativeInt offsetRaw)
    {
        var (db, controller) = CreateControllerWithContext();
        using (db)
        {
            var precioMinimo = (decimal)precioMinRaw.Get;
            var offset = (decimal)offsetRaw.Get;
            var precioEspecial = precioMinimo + offset; // always >= precioMinimo

            // Seed a product with the generated PrecioMinimo
            var producto = new Producto
            {
                ComercioId = 1,
                Nombre = "Producto Test",
                TipoArticulo = "Venta Directa",
                ManejaStock = true,
                CostoProduccion = 0m,
                PrecioLista = precioEspecial + 10m,
                PrecioMinimo = precioMinimo
            };
            db.Productos.Add(producto);
            db.SaveChanges();

            // Create a volume price rule with PrecioEspecial >= PrecioMinimo
            var request = new CreatePrecioVolumenRequest(
                CantidadMinima: 1m,
                PrecioEspecial: precioEspecial);

            var result = controller.CreatePrecioVolumen(producto.Id, request).GetAwaiter().GetResult();

            // Should always be CreatedAtAction (HTTP 201)
            if (result is not CreatedAtActionResult created)
                return false;

            return created.StatusCode == 201;
        }
    }
}
