using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Tenants;

[ApiController]
[Route("api/tenants/productos")]
public class ProductosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public ProductosController(
        AppDbContext db,
        ITenantContext tenantContext,
        IAuditService auditService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    /// <summary>
    /// GET /api/tenants/productos — List productos for the authenticated comercio.
    /// Excludes products of type "Insumo" (Req 6.6: POS excludes Insumo from sale interfaces).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetAll()
    {
        var comercioId = GetComercioId();

        var productos = await _db.Productos
            .Where(p => p.ComercioId == comercioId && p.TipoArticulo != "Insumo")
            .Select(p => new ProductoDto(
                p.Id,
                p.Nombre,
                p.TipoArticulo,
                p.CategoriaId,
                p.ValoresDinamicos,
                p.ManejaStock,
                p.UnidadMedida,
                p.CostoProduccion,
                p.PrecioLista,
                p.PrecioMinimo))
            .ToListAsync();

        return Ok(productos);
    }

    /// <summary>
    /// POST /api/tenants/productos — Create a new producto.
    /// Validates that Ensamblado type must have at least one RecetaProducto (Req 6.5).
    /// Logs creation in audit trail (Req 6.7).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> Create([FromBody] CreateProductoRequest request)
    {
        var comercioId = GetComercioId();
        var usuarioId = GetUsuarioId();

        // Validate TipoArticulo
        var tiposValidos = new[] { "Venta Directa", "Insumo", "Ensamblado" };
        if (!tiposValidos.Contains(request.TipoArticulo))
            return BadRequest(new { error = "TipoArticulo inválido. Valores permitidos: Venta Directa, Insumo, Ensamblado.", code = "INVALID_TIPO_ARTICULO" });

        // Req 6.5: Ensamblado requires at least one RecetaProducto — cannot be created standalone
        if (request.TipoArticulo == "Ensamblado")
            return BadRequest(new { error = "Un producto de tipo Ensamblado requiere al menos un ingrediente en su receta. Use el endpoint de receta después de crear el producto o proporcione receta.", code = "ENSAMBLADO_REQUIRES_RECETA" });

        var producto = new Producto
        {
            ComercioId = comercioId,
            Nombre = request.Nombre,
            TipoArticulo = request.TipoArticulo,
            CategoriaId = request.CategoriaId,
            ValoresDinamicos = request.ValoresDinamicos,
            ManejaStock = request.ManejaStock,
            UnidadMedida = request.UnidadMedida,
            CostoProduccion = request.CostoProduccion,
            PrecioLista = request.PrecioLista,
            PrecioMinimo = request.PrecioMinimo
        };

        _db.Productos.Add(producto);
        await _db.SaveChangesAsync();

        // Req 6.7: Audit log
        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "Crear",
            "Productos",
            producto.Id.ToString(),
            null,
            new
            {
                producto.Id,
                producto.Nombre,
                producto.TipoArticulo,
                producto.CategoriaId,
                producto.ValoresDinamicos,
                producto.ManejaStock,
                producto.UnidadMedida,
                producto.CostoProduccion,
                producto.PrecioLista,
                producto.PrecioMinimo
            });

        var dto = new ProductoDto(
            producto.Id,
            producto.Nombre,
            producto.TipoArticulo,
            producto.CategoriaId,
            producto.ValoresDinamicos,
            producto.ManejaStock,
            producto.UnidadMedida,
            producto.CostoProduccion,
            producto.PrecioLista,
            producto.PrecioMinimo);

        return CreatedAtAction(nameof(GetAll), dto);
    }

    /// <summary>
    /// PUT /api/tenants/productos/{id} — Update an existing producto.
    /// Validates that Ensamblado type must have at least one RecetaProducto (Req 6.5).
    /// Logs update in audit trail (Req 6.7).
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductoRequest request)
    {
        var comercioId = GetComercioId();
        var usuarioId = GetUsuarioId();

        var producto = await _db.Productos
            .FirstOrDefaultAsync(p => p.Id == id && p.ComercioId == comercioId);

        if (producto is null)
            return NotFound(new { error = "Producto no encontrado.", code = "PRODUCTO_NOT_FOUND" });

        // Validate TipoArticulo
        var tiposValidos = new[] { "Venta Directa", "Insumo", "Ensamblado" };
        if (!tiposValidos.Contains(request.TipoArticulo))
            return BadRequest(new { error = "TipoArticulo inválido. Valores permitidos: Venta Directa, Insumo, Ensamblado.", code = "INVALID_TIPO_ARTICULO" });

        // Req 6.5: If updating to Ensamblado, must already have at least one RecetaProducto
        if (request.TipoArticulo == "Ensamblado")
        {
            var hasReceta = await _db.RecetasProducto.AnyAsync(r => r.ProductoFinalId == id);
            if (!hasReceta)
                return BadRequest(new { error = "Un producto de tipo Ensamblado requiere al menos un ingrediente en su receta.", code = "ENSAMBLADO_REQUIRES_RECETA" });
        }

        // Capture previous values for audit
        var valoresAnteriores = new
        {
            producto.Id,
            producto.Nombre,
            producto.TipoArticulo,
            producto.CategoriaId,
            producto.ValoresDinamicos,
            producto.ManejaStock,
            producto.UnidadMedida,
            producto.CostoProduccion,
            producto.PrecioLista,
            producto.PrecioMinimo
        };

        // Apply updates
        producto.Nombre = request.Nombre;
        producto.TipoArticulo = request.TipoArticulo;
        producto.CategoriaId = request.CategoriaId;
        producto.ValoresDinamicos = request.ValoresDinamicos;
        producto.ManejaStock = request.ManejaStock;
        producto.UnidadMedida = request.UnidadMedida;
        producto.CostoProduccion = request.CostoProduccion;
        producto.PrecioLista = request.PrecioLista;
        producto.PrecioMinimo = request.PrecioMinimo;

        await _db.SaveChangesAsync();

        // Req 6.7: Audit log
        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "Actualizar",
            "Productos",
            producto.Id.ToString(),
            valoresAnteriores,
            new
            {
                producto.Id,
                producto.Nombre,
                producto.TipoArticulo,
                producto.CategoriaId,
                producto.ValoresDinamicos,
                producto.ManejaStock,
                producto.UnidadMedida,
                producto.CostoProduccion,
                producto.PrecioLista,
                producto.PrecioMinimo
            });

        var dto = new ProductoDto(
            producto.Id,
            producto.Nombre,
            producto.TipoArticulo,
            producto.CategoriaId,
            producto.ValoresDinamicos,
            producto.ManejaStock,
            producto.UnidadMedida,
            producto.CostoProduccion,
            producto.PrecioLista,
            producto.PrecioMinimo);

        return Ok(dto);
    }

    /// <summary>
    /// DELETE /api/tenants/productos/{id} — Delete a producto.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> Delete(int id)
    {
        var comercioId = GetComercioId();

        var producto = await _db.Productos
            .FirstOrDefaultAsync(p => p.Id == id && p.ComercioId == comercioId);

        if (producto is null)
            return NotFound(new { error = "Producto no encontrado.", code = "PRODUCTO_NOT_FOUND" });

        // Check if product has associated sales
        var hasVentas = await _db.DetalleVentas.AnyAsync(d => d.ProductoId == id);
        if (hasVentas)
            return Conflict(new { error = "No se puede eliminar el producto porque tiene ventas registradas.", code = "PRODUCTO_HAS_VENTAS" });

        _db.Productos.Remove(producto);
        await _db.SaveChangesAsync();

        // Req 15.1: Audit log for product deletion
        var usuarioId = GetUsuarioId();
        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "Eliminar",
            "Productos",
            id.ToString(),
            new { producto.Id, producto.Nombre, producto.TipoArticulo, producto.CategoriaId, producto.PrecioLista, producto.PrecioMinimo },
            null);

        return NoContent();
    }

    /// <summary>
    /// GET /api/tenants/productos/{id}/receta — Get the recipe for an Ensamblado product.
    /// </summary>
    [HttpGet("{id:int}/receta")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetReceta(int id)
    {
        var comercioId = GetComercioId();

        var producto = await _db.Productos
            .FirstOrDefaultAsync(p => p.Id == id && p.ComercioId == comercioId);

        if (producto is null)
            return NotFound(new { error = "Producto no encontrado.", code = "PRODUCTO_NOT_FOUND" });

        var receta = await _db.RecetasProducto
            .Where(r => r.ProductoFinalId == id)
            .Select(r => new RecetaProductoDto(r.Id, r.ProductoFinalId, r.IngredienteId, r.CantidadRequerida))
            .ToListAsync();

        return Ok(receta);
    }

    /// <summary>
    /// POST /api/tenants/productos/{id}/receta — Define or update the recipe for an Ensamblado product.
    /// Replaces all existing recipe entries with the provided list.
    /// </summary>
    [HttpPost("{id:int}/receta")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> SetReceta(int id, [FromBody] List<CreateRecetaRequest> request)
    {
        var comercioId = GetComercioId();

        var producto = await _db.Productos
            .FirstOrDefaultAsync(p => p.Id == id && p.ComercioId == comercioId);

        if (producto is null)
            return NotFound(new { error = "Producto no encontrado.", code = "PRODUCTO_NOT_FOUND" });

        if (request.Count == 0)
            return BadRequest(new { error = "La receta debe contener al menos un ingrediente.", code = "RECETA_EMPTY" });

        // Validate all ingredients belong to the same comercio
        var ingredienteIds = request.Select(r => r.IngredienteId).ToList();
        var validIngredientes = await _db.Productos
            .Where(p => ingredienteIds.Contains(p.Id) && p.ComercioId == comercioId)
            .Select(p => p.Id)
            .ToListAsync();

        var invalidIds = ingredienteIds.Except(validIngredientes).ToList();
        if (invalidIds.Count > 0)
            return BadRequest(new { error = $"Ingredientes no encontrados o no pertenecen al comercio: {string.Join(", ", invalidIds)}", code = "INVALID_INGREDIENTES" });

        // Remove existing recipe entries
        var existingReceta = await _db.RecetasProducto
            .Where(r => r.ProductoFinalId == id)
            .ToListAsync();

        _db.RecetasProducto.RemoveRange(existingReceta);

        // Add new recipe entries
        var newReceta = request.Select(r => new RecetaProducto
        {
            ProductoFinalId = id,
            IngredienteId = r.IngredienteId,
            CantidadRequerida = r.CantidadRequerida
        }).ToList();

        _db.RecetasProducto.AddRange(newReceta);

        // If this product is type Ensamblado or being set as such, ensure TipoArticulo is Ensamblado
        if (producto.TipoArticulo != "Ensamblado")
        {
            producto.TipoArticulo = "Ensamblado";
        }

        await _db.SaveChangesAsync();

        var dtos = newReceta.Select(r => new RecetaProductoDto(r.Id, r.ProductoFinalId, r.IngredienteId, r.CantidadRequerida)).ToList();
        return Ok(dtos);
    }

    // ── PreciosVolumen Endpoints (Req 8.1, 8.4) ─────────────────────────────────

    /// <summary>
    /// GET /api/tenants/productos/{id}/precios-volumen — List volume price rules for a product.
    /// </summary>
    [HttpGet("{id:int}/precios-volumen")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetPreciosVolumen(int id)
    {
        var comercioId = GetComercioId();

        var producto = await _db.Productos
            .FirstOrDefaultAsync(p => p.Id == id && p.ComercioId == comercioId);

        if (producto is null)
            return NotFound(new { error = "Producto no encontrado.", code = "PRODUCTO_NOT_FOUND" });

        var reglas = await _db.PreciosVolumen
            .Where(pv => pv.ProductoId == id)
            .Select(pv => new PrecioVolumenDto(pv.Id, pv.ProductoId, pv.CantidadMinima, pv.PrecioEspecial))
            .ToListAsync();

        return Ok(reglas);
    }

    /// <summary>
    /// POST /api/tenants/productos/{id}/precios-volumen — Create a volume price rule.
    /// Validates PrecioEspecial >= Producto.PrecioMinimo (Req 8.4).
    /// </summary>
    [HttpPost("{id:int}/precios-volumen")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> CreatePrecioVolumen(int id, [FromBody] CreatePrecioVolumenRequest request)
    {
        var comercioId = GetComercioId();

        var producto = await _db.Productos
            .FirstOrDefaultAsync(p => p.Id == id && p.ComercioId == comercioId);

        if (producto is null)
            return NotFound(new { error = "Producto no encontrado.", code = "PRODUCTO_NOT_FOUND" });

        if (request.CantidadMinima <= 0)
            return BadRequest(new { error = "La cantidad mínima debe ser mayor a 0.", code = "INVALID_CANTIDAD_MINIMA" });

        if (request.PrecioEspecial <= 0)
            return BadRequest(new { error = "El precio especial debe ser mayor a 0.", code = "INVALID_PRECIO_ESPECIAL" });

        // Req 8.4: PrecioEspecial must not be less than PrecioMinimo
        if (request.PrecioEspecial < producto.PrecioMinimo)
            return UnprocessableEntity(new { error = "El precio especial no puede ser inferior al precio mínimo del producto.", code = "PRECIO_BELOW_MINIMUM" });

        var regla = new PrecioVolumen
        {
            ProductoId = id,
            CantidadMinima = request.CantidadMinima,
            PrecioEspecial = request.PrecioEspecial
        };

        _db.PreciosVolumen.Add(regla);
        await _db.SaveChangesAsync();

        var dto = new PrecioVolumenDto(regla.Id, regla.ProductoId, regla.CantidadMinima, regla.PrecioEspecial);
        return CreatedAtAction(nameof(GetPreciosVolumen), new { id }, dto);
    }

    /// <summary>
    /// DELETE /api/tenants/productos/{id}/precios-volumen/{reglaId} — Delete a volume price rule.
    /// </summary>
    [HttpDelete("{id:int}/precios-volumen/{reglaId:int}")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> DeletePrecioVolumen(int id, int reglaId)
    {
        var comercioId = GetComercioId();

        var producto = await _db.Productos
            .FirstOrDefaultAsync(p => p.Id == id && p.ComercioId == comercioId);

        if (producto is null)
            return NotFound(new { error = "Producto no encontrado.", code = "PRODUCTO_NOT_FOUND" });

        var regla = await _db.PreciosVolumen
            .FirstOrDefaultAsync(pv => pv.Id == reglaId && pv.ProductoId == id);

        if (regla is null)
            return NotFound(new { error = "Regla de precio por volumen no encontrada.", code = "PRECIO_VOLUMEN_NOT_FOUND" });

        _db.PreciosVolumen.Remove(regla);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// GET /api/tenants/productos/precios-volumen — Obtiene TODOS los precios por volumen
    /// de los productos del comercio autenticado en una sola consulta.
    /// Optimización: elimina la necesidad de N requests individuales por producto.
    /// Retorna un diccionario agrupado por ProductoId.
    /// </summary>
    [HttpGet("precios-volumen")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetAllPreciosVolumen()
    {
        var comercioId = GetComercioId();

        // Obtener IDs de productos del comercio actual
        var productIds = await _db.Productos
            .Where(p => p.ComercioId == comercioId)
            .Select(p => p.Id)
            .ToListAsync();

        // Obtener todas las reglas de precios-volumen para esos productos
        var reglas = await _db.PreciosVolumen
            .Where(pv => productIds.Contains(pv.ProductoId))
            .Select(pv => new PrecioVolumenDto(pv.Id, pv.ProductoId, pv.CantidadMinima, pv.PrecioEspecial))
            .ToListAsync();

        // Agrupar por ProductoId para consumo fácil en el frontend
        var agrupado = reglas
            .GroupBy(r => r.ProductoId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return Ok(agrupado);
    }

    private int GetComercioId() =>
        _tenantContext.ComercioId
            ?? throw new UnauthorizedAccessException("ComercioId not available in tenant context.");

    private int GetUsuarioId() =>
        int.Parse(User.FindFirstValue("sub")!);
}
