using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Admin;

/// <summary>
/// Admin dashboard — global revenue, projections, and commerce status overview.
/// Req 2.6, 20.1, 20.2, 20.3
/// </summary>
[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Policy = "SuperAdminOnly")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /api/admin/dashboard — Global summary: revenue by plan, monthly projection, commerce states.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDashboard()
    {
        // Req 20.1: Total accumulated global revenue broken down by active Plan
        var ingresosPorPlan = await _db.PagosComercio
            .IgnoreQueryFilters()
            .Include(p => p.Comercio)
                .ThenInclude(c => c.Plan)
            .GroupBy(p => p.Comercio.Plan.Nombre)
            .Select(g => new IngresoPorPlanDto(
                g.Key,
                g.Sum(p => p.MontoPagado)))
            .ToListAsync();

        // Req 20.2: Monthly projected total based on active commerces
        var totalMensualProyectado = await _db.Suscripciones
            .IgnoreQueryFilters()
            .Where(s => s.Estado == "Activa" && s.Comercio.Estado == "Activo")
            .SumAsync(s => s.MontoCuota);

        // Req 20.3: List of commerces with state and próximo corte date
        var comercios = await _db.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .Include(c => c.Suscripciones)
            .Select(c => new
            {
                c.Id,
                c.Ruc,
                c.RazonSocial,
                PlanNombre = c.Plan.Nombre,
                c.Estado,
                UltimaSuscripcion = c.Suscripciones
                    .OrderByDescending(s => s.FechaInicio)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var comerciosDto = comercios.Select(c =>
        {
            string estado;
            if (c.Estado == "Suspendido")
                estado = "Suspendido";
            else if (c.UltimaSuscripcion?.Estado == "Por vencer")
                estado = "Por vencer";
            else if (c.UltimaSuscripcion?.Estado == "En mora")
                estado = "En mora";
            else
                estado = "Activo";

            return new ComercioEstadoDto(
                c.Id,
                c.Ruc,
                c.RazonSocial,
                c.PlanNombre,
                estado,
                c.UltimaSuscripcion?.FechaProximoCorte);
        }).ToList();

        var dashboard = new DashboardDto(
            ingresosPorPlan,
            totalMensualProyectado,
            comerciosDto);

        return Ok(dashboard);
    }
}
