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

namespace SaasPOS.Tests.Controllers;

public class PreciosVolumenEndpointTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ProductosController _controller;
    private readonly int _comercioId = 1;

    public PreciosVolumenEndpointTests()
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.ComercioId).Returns(_comercioId);
        tenantContext.Setup(t => t.IsSuperAdmin).Returns(false);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options, tenantContext.Object);

        var auditService = new Mock<IAuditService>();
        _controller = new ProductosController(_db, tenantContext.Object, auditService.Object);

        // Set up HttpContext with claims for GetUsuarioId
        var claims = new[] { new Claim("sub", "1") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<Producto> SeedProducto(decimal precioMinimo = 10m)
    {
        var producto = new Producto
        {
            ComercioId = _comercioId,
            Nombre = "Producto Test",
            TipoArticulo = "Venta Directa",
            PrecioLista = 100m,
            PrecioMinimo = precioMinimo
        };
        _db.Productos.Add(producto);
        await _db.SaveChangesAsync();
        return producto;
    }

    // ── GET Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPreciosVolumen_ProductNotFound_Returns404()
    {
        var result = await _controller.GetPreciosVolumen(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task GetPreciosVolumen_ReturnsEmptyList_WhenNoRules()
    {
        var producto = await SeedProducto();

        var result = await _controller.GetPreciosVolumen(producto.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<List<PrecioVolumenDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetPreciosVolumen_ReturnsExistingRules()
    {
        var producto = await SeedProducto();
        _db.PreciosVolumen.Add(new PrecioVolumen { ProductoId = producto.Id, CantidadMinima = 10, PrecioEspecial = 15 });
        _db.PreciosVolumen.Add(new PrecioVolumen { ProductoId = producto.Id, CantidadMinima = 50, PrecioEspecial = 12 });
        await _db.SaveChangesAsync();

        var result = await _controller.GetPreciosVolumen(producto.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<List<PrecioVolumenDto>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    // ── POST Tests ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePrecioVolumen_ProductNotFound_Returns404()
    {
        var request = new CreatePrecioVolumenRequest(10, 20);

        var result = await _controller.CreatePrecioVolumen(999, request);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CreatePrecioVolumen_CantidadMinimaCero_Returns400()
    {
        var producto = await SeedProducto();
        var request = new CreatePrecioVolumenRequest(0, 20);

        var result = await _controller.CreatePrecioVolumen(producto.Id, request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task CreatePrecioVolumen_PrecioEspecialCero_Returns400()
    {
        var producto = await SeedProducto();
        var request = new CreatePrecioVolumenRequest(10, 0);

        var result = await _controller.CreatePrecioVolumen(producto.Id, request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task CreatePrecioVolumen_PrecioEspecialBelowMinimo_Returns422()
    {
        // Arrange: PrecioMinimo = 10, PrecioEspecial = 5 (below minimum)
        var producto = await SeedProducto(precioMinimo: 10m);
        var request = new CreatePrecioVolumenRequest(10, 5);

        // Act
        var result = await _controller.CreatePrecioVolumen(producto.Id, request);

        // Assert: HTTP 422 with descriptive error
        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Equal(422, unprocessable.StatusCode);

        // Verify error message and code
        var value = unprocessable.Value!;
        var errorProp = value.GetType().GetProperty("error");
        var codeProp = value.GetType().GetProperty("code");
        Assert.Equal("El precio especial no puede ser inferior al precio mínimo del producto.", errorProp!.GetValue(value));
        Assert.Equal("PRECIO_BELOW_MINIMUM", codeProp!.GetValue(value));
    }

    [Fact]
    public async Task CreatePrecioVolumen_PrecioEspecialEqualToMinimo_Succeeds()
    {
        // Arrange: PrecioMinimo = 10, PrecioEspecial = 10 (exactly at minimum)
        var producto = await SeedProducto(precioMinimo: 10m);
        var request = new CreatePrecioVolumenRequest(5, 10);

        // Act
        var result = await _controller.CreatePrecioVolumen(producto.Id, request);

        // Assert: Created (201)
        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<PrecioVolumenDto>(created.Value);
        Assert.Equal(producto.Id, dto.ProductoId);
        Assert.Equal(10m, dto.PrecioEspecial);
        Assert.Equal(5m, dto.CantidadMinima);
    }

    [Fact]
    public async Task CreatePrecioVolumen_PrecioEspecialAboveMinimo_Succeeds()
    {
        var producto = await SeedProducto(precioMinimo: 10m);
        var request = new CreatePrecioVolumenRequest(100, 15);

        var result = await _controller.CreatePrecioVolumen(producto.Id, request);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<PrecioVolumenDto>(created.Value);
        Assert.Equal(15m, dto.PrecioEspecial);
    }

    // ── DELETE Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletePrecioVolumen_ProductNotFound_Returns404()
    {
        var result = await _controller.DeletePrecioVolumen(999, 1);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeletePrecioVolumen_RuleNotFound_Returns404()
    {
        var producto = await SeedProducto();

        var result = await _controller.DeletePrecioVolumen(producto.Id, 999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeletePrecioVolumen_ExistingRule_ReturnsNoContent()
    {
        var producto = await SeedProducto();
        var regla = new PrecioVolumen { ProductoId = producto.Id, CantidadMinima = 10, PrecioEspecial = 20 };
        _db.PreciosVolumen.Add(regla);
        await _db.SaveChangesAsync();

        var result = await _controller.DeletePrecioVolumen(producto.Id, regla.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _db.PreciosVolumen.Where(pv => pv.ProductoId == producto.Id).ToListAsync());
    }
}
