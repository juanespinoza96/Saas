using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Tenants;

/// <summary>
/// GET /api/tenants/configuracion — Branch configuration (per-sucursal).
/// PUT /api/tenants/configuracion/{sucursalId} — Update branch configuration toggles.
/// </summary>
[ApiController]
[Route("api/tenants/configuracion")]
[Authorize(Policy = "TenantAccess")]
public class ConfiguracionController : ControllerBase
{
    private readonly IConfiguracionSucursalService _configuracionService;
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITimezoneService _timezoneService;

    public ConfiguracionController(
        IConfiguracionSucursalService configuracionService,
        AppDbContext db,
        ITenantContext tenantContext,
        ITimezoneService timezoneService)
    {
        _configuracionService = configuracionService;
        _db = db;
        _tenantContext = tenantContext;
        _timezoneService = timezoneService;
    }

    /// <summary>
    /// GET /api/tenants/configuracion — Returns branch configuration + plan level.
    /// Resolves sucursalId from: 1) query param, 2) JWT claim, 3) primera sucursal del comercio (fallback para Dueño).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? sucursalId)
    {
        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "ComercioId not available.", code = "UNAUTHORIZED" });

        // Prioridad: 1) query param, 2) JWT claim, 3) primera sucursal del comercio
        var resolvedSucursalId = sucursalId ?? _tenantContext.SucursalId;

        if (resolvedSucursalId is null)
        {
            // Fallback para Dueño (SucursalId NULL): usar la primera sucursal del comercio
            var primeraSucursal = await _db.Sucursales
                .Where(s => s.ComercioId == comercioId.Value)
                .OrderBy(s => s.Id)
                .Select(s => s.Id)
                .FirstOrDefaultAsync();

            if (primeraSucursal == 0)
                return BadRequest(new { error = "El comercio no tiene sucursales registradas.", code = "NO_SUCURSALES" });

            resolvedSucursalId = primeraSucursal;
        }

        try
        {
            var config = await _configuracionService.GetBySucursalIdAsync(resolvedSucursalId.Value, comercioId.Value);

            // Fetch plan level and UsaFacturacionSRI from Comercio+Plan
            var comercio = await _db.Comercios
                .IgnoreQueryFilters()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == comercioId.Value);

            var planNivel = comercio?.Plan?.Nombre ?? "Básico";
            var usaFacturacionSRI = comercio?.UsaFacturacionSRI ?? false;

            var response = new ConfiguracionSucursalResponseDto(
                SucursalId: config.SucursalId,
                EsBarEscolar: config.EsBarEscolar,
                MostrarBotonCliente: config.MostrarBotonCliente,
                PermiteVentaEnNegativo: config.PermiteVentaEnNegativo,
                ImpresionAutomaticaTicket: config.ImpresionAutomaticaTicket,
                PermitePrecioNegociado: config.PermitePrecioNegociado,
                MostrarVentasAlCajero: config.MostrarVentasAlCajero,
                PlanNivel: planNivel,
                UsaFacturacionSRI: usaFacturacionSRI);

            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Sucursal no encontrada.", code = "SUCURSAL_NOT_FOUND" });
        }
    }

    /// <summary>
    /// PUT /api/tenants/configuracion/{sucursalId} — Update branch configuration toggles.
    /// Only Gerente and Dueño can write. Gerente can only modify their assigned branch.
    /// </summary>
    [HttpPut("{sucursalId:int}")]
    public async Task<IActionResult> Update(int sucursalId, [FromBody] UpdateConfiguracionSucursalRequest request)
    {
        // Validate sucursalId > 0
        if (sucursalId <= 0)
            return BadRequest(new { error = "El identificador de sucursal debe ser un entero mayor a 0.", code = "INVALID_SUCURSAL_ID" });

        // Validate model state (all 6 boolean fields required)
        if (!ModelState.IsValid)
            return BadRequest(new { error = "El body debe contener los 6 campos booleanos requeridos.", code = "VALIDATION_ERROR" });

        // Get role from JWT
        var role = User.FindFirstValue("role");

        // Only Gerente and Dueño can write
        if (role != "Gerente" && role != "Dueño")
            return StatusCode(403, new { error = "No tiene permisos para modificar la configuración.", code = "ACCESS_DENIED" });

        // If Gerente: validate that sucursalId matches their assigned sucursal
        if (role == "Gerente")
        {
            var gerenteSucursalId = _tenantContext.SucursalId;
            if (gerenteSucursalId is null || gerenteSucursalId.Value != sucursalId)
                return StatusCode(403, new { error = "Un Gerente solo puede modificar la configuración de su sucursal asignada.", code = "ACCESS_DENIED" });
        }

        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "ComercioId not available.", code = "UNAUTHORIZED" });

        var usuarioId = GetUsuarioId();

        try
        {
            var result = await _configuracionService.UpdateAsync(sucursalId, comercioId.Value, usuarioId, request);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Sucursal no encontrada.", code = "SUCURSAL_NOT_FOUND" });
        }
    }

    /// <summary>
    /// PATCH /api/tenants/configuracion/zona-horaria-defecto — Actualiza la zona horaria por defecto del comercio.
    /// Solo el Dueño puede ejecutar esta acción. Acepta NULL/vacío para eliminar la zona por defecto.
    /// </summary>
    [HttpPatch("zona-horaria-defecto")]
    public async Task<IActionResult> UpdateZonaHorariaDefecto([FromBody] UpdateZonaHorariaDefectoRequest request)
    {
        // Solo el Dueño puede modificar la zona horaria por defecto del comercio
        var role = User.FindFirstValue("role");
        if (role != "Dueño")
            return StatusCode(403, new { error = "Solo el Dueño puede modificar la zona horaria por defecto del comercio.", code = "ACCESS_DENIED" });

        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "ComercioId not available.", code = "UNAUTHORIZED" });

        // Si el valor es null o vacío, se elimina la zona por defecto (se establece en NULL)
        string? zonaHoraria = string.IsNullOrWhiteSpace(request.ZonaHorariaDefecto)
            ? null
            : request.ZonaHorariaDefecto.Trim();

        // Si se proporciona un valor no nulo, validar que sea una zona IANA reconocida
        if (zonaHoraria is not null && !_timezoneService.IsValidTimeZone(zonaHoraria))
            return BadRequest(new { error = "La zona horaria proporcionada no es válida. Debe ser un identificador IANA reconocido.", code = "INVALID_TIMEZONE" });

        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == comercioId.Value);

        if (comercio is null)
            return NotFound(new { error = "Comercio no encontrado.", code = "COMERCIO_NOT_FOUND" });

        comercio.ZonaHorariaDefecto = zonaHoraria;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Zona horaria por defecto actualizada correctamente.", zonaHorariaDefecto = comercio.ZonaHorariaDefecto });
    }

    private int GetUsuarioId() =>
        int.Parse(User.FindFirstValue("sub")!);
}

/// <summary>
/// DTO para la actualización de la zona horaria por defecto del comercio.
/// </summary>
public class UpdateZonaHorariaDefectoRequest
{
    /// <summary>
    /// Zona horaria IANA (ej: "America/Guayaquil"). Null o vacío para eliminar la zona por defecto.
    /// </summary>
    public string? ZonaHorariaDefecto { get; set; }
}
