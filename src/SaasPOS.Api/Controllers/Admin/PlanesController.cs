using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/planes")]
[Authorize(Policy = "SuperAdminOnly")]
public class PlanesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PlanesController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /api/admin/planes — List all plans (Req 1.1)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var planes = await _db.Planes
            .IgnoreQueryFilters()
            .Select(p => new PlanDto(p.Id, p.Nombre, p.Precio, p.LimiteUsuarios, p.LimiteAtributos))
            .ToListAsync();

        return Ok(planes);
    }

    /// <summary>
    /// PUT /api/admin/planes/{id} — Update plan price/limits (Req 1.2)
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePlanRequest request)
    {
        var plan = await _db.Planes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (plan is null)
            return NotFound(new { error = "Plan no encontrado.", code = "PLAN_NOT_FOUND" });

        plan.Precio = request.Precio;
        plan.LimiteUsuarios = request.LimiteUsuarios;
        plan.LimiteAtributos = request.LimiteAtributos;

        await _db.SaveChangesAsync();

        return Ok(new PlanDto(plan.Id, plan.Nombre, plan.Precio, plan.LimiteUsuarios, plan.LimiteAtributos));
    }
}
