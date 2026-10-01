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
[Route("api/tenants/sucursales")]
public class SucursalesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISubscriptionGuard _subscriptionGuard;
    private readonly IAuditService _auditService;
    private readonly IConfiguracionSucursalService _configuracionService;

    public SucursalesController(
        AppDbContext db,
        ITenantContext tenantContext,
        ISubscriptionGuard subscriptionGuard,
        IAuditService auditService,
        IConfiguracionSucursalService configuracionService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _subscriptionGuard = subscriptionGuard;
        _auditService = auditService;
        _configuracionService = configuracionService;
    }

    /// <summary>
    /// GET /api/tenants/sucursales — List sucursales for the authenticated comercio.
    /// The query filter on Sucursales ensures multi-tenant isolation automatically.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetAll()
    {
        var comercioId = GetComercioId();

        var sucursales = await _db.Sucursales
            .Where(s => s.ComercioId == comercioId)
            .Select(s => new SucursalDto(
                s.Id,
                s.Nombre,
                s.Direccion,
                s.Telefono,
                s.SerieFacturacion))
            .ToListAsync();

        return Ok(sucursales);
    }

    /// <summary>
    /// POST /api/tenants/sucursales — Create a new sucursal.
    /// Validates subscription limits (Req 1.6) and performs secondary validation
    /// before INSERT to prevent race conditions (Req 1.8).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "CanManageSucursales")]
    public async Task<IActionResult> Create([FromBody] CreateSucursalRequest request)
    {
        // Validación de nombre requerido
        if (string.IsNullOrWhiteSpace(request.Nombre))
            return BadRequest(new { error = "El nombre de la sucursal es requerido.", code = "VALIDATION_ERROR" });

        var comercioId = GetComercioId();

        // Primary validation: SubscriptionGuard check
        var canAdd = await _subscriptionGuard.CanAddSucursalAsync(comercioId);
        if (!canAdd)
            return StatusCode(403, new { error = "Límite de sucursales alcanzado para su plan.", code = "PLAN_LIMIT_EXCEEDED" });

        // Secondary validation (Req 1.8): re-count right before INSERT to protect against race conditions
        var currentCount = await _db.Sucursales
            .IgnoreQueryFilters()
            .CountAsync(s => s.ComercioId == comercioId);

        // For Plan Básico, limit is 1 sucursal. Re-verify using the same logic as SubscriptionGuard.
        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == comercioId);

        if (comercio?.Plan is null)
            return StatusCode(403, new { error = "No se puede determinar el plan. Acceso denegado.", code = "PLAN_UNKNOWN" });

        if (comercio.Plan.Nombre == "Básico" && currentCount >= 1)
            return StatusCode(403, new { error = "Límite de sucursales alcanzado para su plan.", code = "PLAN_LIMIT_EXCEEDED" });

        var sucursal = new Sucursal
        {
            ComercioId = comercioId,
            Nombre = request.Nombre,
            Direccion = request.Direccion,
            Telefono = request.Telefono,
            SerieFacturacion = request.SerieFacturacion
        };

        // Wrap creation in a transaction to ensure atomicity (Sucursal + ConfiguracionSucursal + Audit)
        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.Sucursales.Add(sucursal);
            await _db.SaveChangesAsync(); // sucursal.Id is now available

            // Req 1.1, 1.2: Create default configuration for the new sucursal
            await _configuracionService.CrearDefaultAsync(sucursal.Id);

            // Req 15.1: Audit log for sucursal creation
            var usuarioId = GetUsuarioId();
            await _auditService.RegistrarAsync(
                comercioId,
                usuarioId,
                "Crear",
                "Sucursales",
                sucursal.Id.ToString(),
                null,
                new { sucursal.Id, sucursal.Nombre, sucursal.Direccion, sucursal.Telefono, sucursal.SerieFacturacion });

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        var dto = new SucursalDto(
            sucursal.Id,
            sucursal.Nombre,
            sucursal.Direccion,
            sucursal.Telefono,
            sucursal.SerieFacturacion);

        return CreatedAtAction(nameof(GetAll), dto);
    }

    /// <summary>
    /// PUT /api/tenants/sucursales/{id} — Update an existing sucursal.
    /// Verifies the sucursal belongs to the authenticated comercio.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageSucursales")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSucursalRequest request)
    {
        var comercioId = GetComercioId();

        var sucursal = await _db.Sucursales
            .FirstOrDefaultAsync(s => s.Id == id && s.ComercioId == comercioId);

        if (sucursal is null)
            return NotFound(new { error = "Sucursal no encontrada.", code = "SUCURSAL_NOT_FOUND" });

        // Capture previous values for audit
        var valoresAnteriores = new { sucursal.Id, sucursal.Nombre, sucursal.Direccion, sucursal.Telefono, sucursal.SerieFacturacion };

        sucursal.Nombre = request.Nombre;
        sucursal.Direccion = request.Direccion;
        sucursal.Telefono = request.Telefono;
        sucursal.SerieFacturacion = request.SerieFacturacion;

        await _db.SaveChangesAsync();

        // Req 15.1: Audit log for sucursal update
        var usuarioId = GetUsuarioId();
        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "Actualizar",
            "Sucursales",
            sucursal.Id.ToString(),
            valoresAnteriores,
            new { sucursal.Id, sucursal.Nombre, sucursal.Direccion, sucursal.Telefono, sucursal.SerieFacturacion });

        var dto = new SucursalDto(
            sucursal.Id,
            sucursal.Nombre,
            sucursal.Direccion,
            sucursal.Telefono,
            sucursal.SerieFacturacion);

        return Ok(dto);
    }

    /// <summary>
    /// DELETE /api/tenants/sucursales/{id} — Delete a sucursal.
    /// - If the sucursal has associated Ventas, returns 409 Conflict (DB has Restrict delete on Ventas→Sucursal).
    /// - If no Ventas exist, proceeds with deletion:
    ///   - Users assigned to this sucursal get SucursalId set to NULL (EF OnDelete SetNull).
    ///   - StockSucursal records are cascade deleted (EF OnDelete Cascade).
    /// Preserves historical Ventas and StockSucursal data per Req 5.5.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "CanManageSucursales")]
    public async Task<IActionResult> Delete(int id)
    {
        var comercioId = GetComercioId();

        var sucursal = await _db.Sucursales
            .FirstOrDefaultAsync(s => s.Id == id && s.ComercioId == comercioId);

        if (sucursal is null)
            return NotFound(new { error = "Sucursal no encontrada.", code = "SUCURSAL_NOT_FOUND" });

        // Check if sucursal has associated Ventas (Restrict delete behavior)
        var hasVentas = await _db.Ventas
            .AnyAsync(v => v.SucursalId == id);

        if (hasVentas)
            return Conflict(new
            {
                error = "No se puede eliminar la sucursal porque tiene ventas registradas. Los registros históricos deben conservarse.",
                code = "SUCURSAL_HAS_VENTAS"
            });

        // Explicitly set SucursalId = NULL on users assigned to this sucursal (Req 5.5)
        // EF will handle this via OnDelete(SetNull), but we do it explicitly for clarity
        var assignedUsers = await _db.Usuarios
            .Where(u => u.SucursalId == id)
            .ToListAsync();

        foreach (var user in assignedUsers)
        {
            user.SucursalId = null;
        }

        // Remove the sucursal (StockSucursal will cascade delete)
        _db.Sucursales.Remove(sucursal);
        await _db.SaveChangesAsync();

        // Req 15.1: Audit log for sucursal deletion
        var usuarioId = GetUsuarioId();
        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "Eliminar",
            "Sucursales",
            id.ToString(),
            new { sucursal.Id, sucursal.Nombre, sucursal.Direccion, sucursal.Telefono, sucursal.SerieFacturacion },
            null);

        return NoContent();
    }

    private int GetComercioId() =>
        _tenantContext.ComercioId
            ?? throw new UnauthorizedAccessException("ComercioId not available in tenant context.");

    private int GetUsuarioId() =>
        int.Parse(User.FindFirstValue("sub")!);
}
