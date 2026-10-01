using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Tenants;

/// <summary>
/// Controller for SRI electronic invoice operations.
/// Handles emission and status queries for Factura Electrónica.
/// </summary>
[ApiController]
[Route("api/tenants/ventas")]
public class FacturacionSRIController : ControllerBase
{
    private readonly ISRIService _sriService;
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;

    public FacturacionSRIController(
        ISRIService sriService,
        AppDbContext db,
        ITenantContext tenantContext)
    {
        _sriService = sriService;
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// POST /api/tenants/ventas/{id}/emitir-factura — Emit electronic invoice to SRI.
    /// Req 11.1-11.7: Validates access, builds XML, signs, sends, and updates status.
    /// </summary>
    [HttpPost("{id:int}/emitir-factura")]
    [Authorize(Policy = "CanSell")]
    public async Task<IActionResult> EmitirFactura(int id)
    {
        var comercioId = GetComercioId();

        // Verify venta exists and belongs to tenant
        var ventaExists = await _db.Ventas
            .AnyAsync(v => v.Id == id && v.ComercioId == comercioId);

        if (!ventaExists)
            return NotFound(new { error = "Venta no encontrada.", code = "VENTA_NOT_FOUND" });

        var result = await _sriService.EmitirFacturaElectronicaAsync(id, comercioId);

        if (result.Success)
        {
            return Ok(new
            {
                success = true,
                claveAcceso = result.ClaveAcceso,
                estado = result.Estado
            });
        }

        // Differentiate blocked vs error
        if (result.Estado == "Bloqueado")
        {
            return StatusCode(403, new
            {
                error = result.MensajeError,
                code = "FACTURACION_BLOQUEADA"
            });
        }

        return UnprocessableEntity(new
        {
            error = result.MensajeError,
            code = "SRI_ERROR",
            estado = result.Estado,
            claveAcceso = result.ClaveAcceso
        });
    }

    /// <summary>
    /// GET /api/tenants/ventas/{id}/estado-sri — Query SRI authorization status.
    /// </summary>
    [HttpGet("{id:int}/estado-sri")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> ConsultarEstadoSRI(int id)
    {
        var comercioId = GetComercioId();

        var venta = await _db.Ventas
            .Where(v => v.Id == id && v.ComercioId == comercioId)
            .Select(v => new { v.EstadoSRI })
            .FirstOrDefaultAsync();

        if (venta is null)
            return NotFound(new { error = "Venta no encontrada.", code = "VENTA_NOT_FOUND" });

        return Ok(new
        {
            ventaId = id,
            estadoSRI = venta.EstadoSRI
        });
    }

    private int GetComercioId() =>
        _tenantContext.ComercioId
            ?? throw new UnauthorizedAccessException("ComercioId not available in tenant context.");
}
