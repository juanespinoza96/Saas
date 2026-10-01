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
/// Property-based tests for Ensamblado product requiring at least one recipe entry.
/// **Validates: Requirements 6.5**
/// </summary>
public class Property6_EnsambladoRequiresRecetaTests
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

        // Seed a comercio so FK constraints are satisfied
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
    /// Property 6: For any valid product data with TipoArticulo="Ensamblado" and no recipe,
    /// the Create endpoint should always reject the request with 400 and code ENSAMBLADO_REQUIRES_RECETA.
    /// **Validates: Requirements 6.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool EnsambledoCreate_WithoutReceta_AlwaysRejected(
        NonEmptyString nombre,
        PositiveInt precioLista,
        PositiveInt precioMinimo,
        bool manejaStock)
    {
        var (db, controller) = CreateControllerWithContext();
        using (db)
        {
            var request = new CreateProductoRequest(
                Nombre: nombre.Get,
                TipoArticulo: "Ensamblado",
                CategoriaId: null,
                ValoresDinamicos: null,
                ManejaStock: manejaStock,
                UnidadMedida: "unidad",
                CostoProduccion: 0m,
                PrecioLista: precioLista.Get,
                PrecioMinimo: precioMinimo.Get);

            var result = controller.Create(request).GetAwaiter().GetResult();

            // Must always be BadRequest
            if (result is not BadRequestObjectResult badRequest)
                return false;

            return badRequest.StatusCode == 400;
        }
    }

    /// <summary>
    /// Property 6: For any existing non-Ensamblado product being updated to Ensamblado
    /// without existing recipe entries, the Update endpoint should reject with 400.
    /// **Validates: Requirements 6.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool EnsambledoUpdate_WithoutReceta_AlwaysRejected(
        NonEmptyString nombre,
        PositiveInt precioLista,
        bool manejaStock)
    {
        var (db, controller) = CreateControllerWithContext();
        using (db)
        {
            // Seed a product with type "Venta Directa" (not Ensamblado)
            var producto = new Producto
            {
                ComercioId = 1,
                Nombre = "Producto Original",
                TipoArticulo = "Venta Directa",
                ManejaStock = true,
                CostoProduccion = 5m,
                PrecioLista = 10m,
                PrecioMinimo = 8m
            };
            db.Productos.Add(producto);
            db.SaveChanges();

            // Attempt to update it to Ensamblado without any RecetaProducto
            var request = new UpdateProductoRequest(
                Nombre: nombre.Get,
                TipoArticulo: "Ensamblado",
                CategoriaId: null,
                ValoresDinamicos: null,
                ManejaStock: manejaStock,
                UnidadMedida: "unidad",
                CostoProduccion: 0m,
                PrecioLista: precioLista.Get,
                PrecioMinimo: 1m);

            var result = controller.Update(producto.Id, request).GetAwaiter().GetResult();

            // Must always be BadRequest
            if (result is not BadRequestObjectResult badRequest)
                return false;

            return badRequest.StatusCode == 400;
        }
    }
}
