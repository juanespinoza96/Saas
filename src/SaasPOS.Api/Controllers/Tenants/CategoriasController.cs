using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Tenants;

[ApiController]
[Route("api/tenants/categorias")]
public class CategoriasController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISubscriptionGuard _subscriptionGuard;

    public CategoriasController(
        AppDbContext db,
        ITenantContext tenantContext,
        ISubscriptionGuard subscriptionGuard)
    {
        _db = db;
        _tenantContext = tenantContext;
        _subscriptionGuard = subscriptionGuard;
    }

    /// <summary>
    /// GET /api/tenants/categorias — List categorías for the authenticated comercio.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetAll()
    {
        var comercioId = GetComercioId();

        var categorias = await _db.Categorias
            .Where(c => c.ComercioId == comercioId)
            .Select(c => new CategoriaDto(c.Id, c.Nombre))
            .ToListAsync();

        return Ok(categorias);
    }

    /// <summary>
    /// POST /api/tenants/categorias — Create a new categoría.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> Create([FromBody] CreateCategoriaRequest request)
    {
        var comercioId = GetComercioId();

        var categoria = new Categoria
        {
            ComercioId = comercioId,
            Nombre = request.Nombre
        };

        _db.Categorias.Add(categoria);
        await _db.SaveChangesAsync();

        var dto = new CategoriaDto(categoria.Id, categoria.Nombre);
        return CreatedAtAction(nameof(GetAll), dto);
    }

    /// <summary>
    /// PUT /api/tenants/categorias/{id} — Update an existing categoría.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoriaRequest request)
    {
        var comercioId = GetComercioId();

        var categoria = await _db.Categorias
            .FirstOrDefaultAsync(c => c.Id == id && c.ComercioId == comercioId);

        if (categoria is null)
            return NotFound(new { error = "Categoría no encontrada.", code = "CATEGORIA_NOT_FOUND" });

        categoria.Nombre = request.Nombre;
        await _db.SaveChangesAsync();

        var dto = new CategoriaDto(categoria.Id, categoria.Nombre);
        return Ok(dto);
    }

    /// <summary>
    /// GET /api/tenants/categorias/{id}/atributos — List atributos for a categoría.
    /// </summary>
    [HttpGet("{id:int}/atributos")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetAtributos(int id)
    {
        var comercioId = GetComercioId();

        var categoria = await _db.Categorias
            .FirstOrDefaultAsync(c => c.Id == id && c.ComercioId == comercioId);

        if (categoria is null)
            return NotFound(new { error = "Categoría no encontrada.", code = "CATEGORIA_NOT_FOUND" });

        var atributos = await _db.AtributosCategoria
            .Where(a => a.CategoriaId == id)
            .Select(a => new AtributoCategoriaDto(a.Id, a.CategoriaId, a.NombreAtributo, a.TipoDato))
            .ToListAsync();

        return Ok(atributos);
    }

    /// <summary>
    /// POST /api/tenants/categorias/{id}/atributos — Add an attribute to a categoría.
    /// Enforces plan limits via ISubscriptionGuard.
    /// </summary>
    [HttpPost("{id:int}/atributos")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> AddAtributo(int id, [FromBody] CreateAtributoRequest request)
    {
        var comercioId = GetComercioId();

        var categoria = await _db.Categorias
            .FirstOrDefaultAsync(c => c.Id == id && c.ComercioId == comercioId);

        if (categoria is null)
            return NotFound(new { error = "Categoría no encontrada.", code = "CATEGORIA_NOT_FOUND" });

        // Enforce plan limits (Req 6.3)
        var canAdd = await _subscriptionGuard.CanAddAtributoCategoriaAsync(comercioId, id);
        if (!canAdd)
            return StatusCode(403, new { error = "Límite de atributos por categoría alcanzado para su plan.", code = "PLAN_LIMIT_EXCEEDED" });

        var atributo = new AtributoCategoria
        {
            CategoriaId = id,
            NombreAtributo = request.NombreAtributo,
            TipoDato = request.TipoDato
        };

        _db.AtributosCategoria.Add(atributo);
        await _db.SaveChangesAsync();

        var dto = new AtributoCategoriaDto(atributo.Id, atributo.CategoriaId, atributo.NombreAtributo, atributo.TipoDato);
        return CreatedAtAction(nameof(GetAtributos), new { id }, dto);
    }

    /// <summary>
    /// DELETE /api/tenants/categorias/{id}/atributos/{atributoId} — Remove an attribute from a categoría.
    /// </summary>
    [HttpDelete("{id:int}/atributos/{atributoId:int}")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<IActionResult> DeleteAtributo(int id, int atributoId)
    {
        var comercioId = GetComercioId();

        var categoria = await _db.Categorias
            .FirstOrDefaultAsync(c => c.Id == id && c.ComercioId == comercioId);

        if (categoria is null)
            return NotFound(new { error = "Categoría no encontrada.", code = "CATEGORIA_NOT_FOUND" });

        var atributo = await _db.AtributosCategoria
            .FirstOrDefaultAsync(a => a.Id == atributoId && a.CategoriaId == id);

        if (atributo is null)
            return NotFound(new { error = "Atributo no encontrado.", code = "ATRIBUTO_NOT_FOUND" });

        _db.AtributosCategoria.Remove(atributo);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private int GetComercioId() =>
        _tenantContext.ComercioId
            ?? throw new UnauthorizedAccessException("ComercioId not available in tenant context.");
}
