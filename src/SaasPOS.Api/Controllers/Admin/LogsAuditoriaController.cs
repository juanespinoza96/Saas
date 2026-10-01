using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Admin;

/// <summary>
/// Provides SuperAdmin-exclusive access to LogsAuditoria (Req 4.2, 15.4).
/// The TenantContextMiddleware sets AdminTenantContext for /api/admin/ routes,
/// and LogsAuditoria has no tenant query filter, enabling cross-tenant access.
/// Uses IgnoreQueryFilters() for safety against future filter additions.
/// </summary>
[ApiController]
[Route("api/admin/logs")]
[Authorize(Policy = "SuperAdminOnly")]
public class LogsAuditoriaController : ControllerBase
{
    private readonly AppDbContext _db;

    public LogsAuditoriaController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /api/admin/logs — List audit logs with optional filters and pagination.
    /// Only accessible by SuperAdmin (Req 15.4); other roles receive HTTP 403.
    /// Uses IgnoreQueryFilters() for cross-tenant access (Req 4.2).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? comercioId,
        [FromQuery] string? tablaAfectada,
        [FromQuery] string? accion,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _db.LogsAuditoria
            .IgnoreQueryFilters()
            .AsQueryable();

        if (comercioId.HasValue)
            query = query.Where(l => l.ComercioId == comercioId.Value);

        if (!string.IsNullOrWhiteSpace(tablaAfectada))
            query = query.Where(l => l.TablaAfectada == tablaAfectada);

        if (!string.IsNullOrWhiteSpace(accion))
            query = query.Where(l => l.Accion == accion);

        var totalCount = await query.CountAsync();

        var logs = await query
            .OrderByDescending(l => l.FechaHora)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LogAuditoriaDto(
                l.Id,
                l.ComercioId,
                l.UsuarioId,
                l.Accion,
                l.FechaHora,
                l.TablaAfectada,
                l.RegistroId,
                l.ValoresAnteriores,
                l.ValoresNuevos))
            .ToListAsync();

        var result = new PaginatedResult<LogAuditoriaDto>(
            logs,
            totalCount,
            page,
            pageSize);

        return Ok(result);
    }
}
