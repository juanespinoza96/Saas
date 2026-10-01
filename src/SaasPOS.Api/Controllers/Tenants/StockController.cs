using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Tenants;

/// <summary>
/// Endpoints de inventario/stock por sucursal.
/// GET /api/tenants/stock/sucursal/{sucursalId} — Listar stock de una sucursal.
/// PATCH /api/tenants/stock/{productoId}/sucursal/{sucursalId} — Ajustar stock de un producto.
/// </summary>
[ApiController]
[Route("api/tenants/stock")]
[Authorize(Policy = "TenantAccess")]
public class StockController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IStockService _stockService;

    public StockController(
        AppDbContext db,
        ITenantContext tenantContext,
        IStockService stockService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _stockService = stockService;
    }

    /// <summary>
    /// GET /api/tenants/stock/sucursal/{sucursalId} — Retorna el stock de todos los productos para una sucursal.
    /// </summary>
    [HttpGet("sucursal/{sucursalId:int}")]
    public async Task<IActionResult> GetBySucursal(int sucursalId)
    {
        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "ComercioId not available.", code = "UNAUTHORIZED" });

        // Verificar que la sucursal pertenece al comercio del usuario
        var sucursalExiste = await _db.Sucursales
            .AnyAsync(s => s.Id == sucursalId && s.ComercioId == comercioId.Value);

        if (!sucursalExiste)
            return NotFound(new { error = "Sucursal no encontrada.", code = "SUCURSAL_NOT_FOUND" });

        // Obtener stock con datos del producto
        var stock = await _db.StockSucursal
            .Where(ss => ss.SucursalId == sucursalId && ss.Producto.ComercioId == comercioId.Value)
            .Select(ss => new
            {
                productoId = ss.ProductoId,
                productoNombre = ss.Producto.Nombre,
                cantidadFisica = ss.CantidadFisica,
                unidadMedida = ss.Producto.UnidadMedida
            })
            .OrderBy(x => x.productoNombre)
            .ToListAsync();

        return Ok(stock);
    }

    /// <summary>
    /// PATCH /api/tenants/stock/{productoId}/sucursal/{sucursalId} — Ajustar stock de un producto en una sucursal.
    /// Solo roles Dueño, Gerente y Bodeguero pueden ajustar stock.
    /// </summary>
    [HttpPatch("{productoId:int}/sucursal/{sucursalId:int}")]
    [Authorize(Policy = "CanManageStock")]
    public async Task<IActionResult> AjustarStock(int productoId, int sucursalId, [FromBody] AjustarStockRequest request)
    {
        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "ComercioId not available.", code = "UNAUTHORIZED" });

        if (request.CantidadFisica < 0)
            return BadRequest(new { error = "La cantidad no puede ser negativa.", code = "INVALID_CANTIDAD" });

        // Verificar que la sucursal pertenece al comercio
        var sucursalExiste = await _db.Sucursales
            .AnyAsync(s => s.Id == sucursalId && s.ComercioId == comercioId.Value);

        if (!sucursalExiste)
            return NotFound(new { error = "Sucursal no encontrada.", code = "SUCURSAL_NOT_FOUND" });

        // Verificar que el producto pertenece al comercio
        var productoExiste = await _db.Productos
            .AnyAsync(p => p.Id == productoId && p.ComercioId == comercioId.Value);

        if (!productoExiste)
            return NotFound(new { error = "Producto no encontrado.", code = "PRODUCTO_NOT_FOUND" });

        var usuarioId = int.Parse(User.FindFirstValue("sub")!);

        await _stockService.AjustarStockAsync(productoId, sucursalId, request.CantidadFisica, usuarioId, comercioId.Value);

        return Ok(new { message = "Stock ajustado correctamente." });
    }
}

/// <summary>
/// Request body para ajustar stock.
/// </summary>
public record AjustarStockRequest(decimal CantidadFisica);
