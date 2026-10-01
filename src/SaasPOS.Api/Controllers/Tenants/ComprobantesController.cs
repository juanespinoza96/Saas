using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Controllers.Tenants;

/// <summary>
/// GET /api/tenants/comprobantes/configuracion — Obtener tipos de comprobante habilitados del comercio.
/// PUT /api/tenants/comprobantes/configuracion — Actualizar tipos de comprobante habilitados.
/// </summary>
[ApiController]
[Route("api/tenants/comprobantes")]
[Authorize(Policy = "TenantAccess")]
public class ComprobantesController : ControllerBase
{
    private readonly IComprobantesConfigService _comprobantesConfigService;
    private readonly ISubscriptionGuard _subscriptionGuard;
    private readonly ITenantContext _tenantContext;

    public ComprobantesController(
        IComprobantesConfigService comprobantesConfigService,
        ISubscriptionGuard subscriptionGuard,
        ITenantContext tenantContext)
    {
        _comprobantesConfigService = comprobantesConfigService;
        _subscriptionGuard = subscriptionGuard;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// GET /api/tenants/comprobantes/configuracion — Retorna los tipos de comprobante con su estado de habilitación.
    /// Solo accesible para Dueño y Gerente.
    /// </summary>
    [HttpGet("configuracion")]
    public async Task<IActionResult> GetConfiguracion()
    {
        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "ComercioId not available.", code = "UNAUTHORIZED" });

        // Solo Dueño y Gerente pueden consultar la configuración de comprobantes
        var role = User.FindFirstValue("role");
        if (role != "Dueño" && role != "Gerente")
            return StatusCode(403, new { error = "No tiene permisos para ver la configuración de comprobantes.", code = "ACCESS_DENIED" });

        var habilitados = await _comprobantesConfigService.GetHabilitadosAsync(comercioId.Value);
        return Ok(habilitados);
    }

    /// <summary>
    /// PUT /api/tenants/comprobantes/configuracion — Actualiza los tipos de comprobante habilitados.
    /// Procesa cada tipo individualmente: habilita o deshabilita según el estado recibido.
    /// Valida prerrequisitos para Factura Electrónica (plan + firma SRI) y el invariante de mínimo 1 habilitado.
    /// Solo accesible para Dueño y Gerente.
    /// </summary>
    [HttpPut("configuracion")]
    public async Task<IActionResult> UpdateConfiguracion([FromBody] UpdateComprobantesRequest request)
    {
        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "ComercioId not available.", code = "UNAUTHORIZED" });

        // Solo Dueño y Gerente pueden modificar la configuración de comprobantes
        var role = User.FindFirstValue("role");
        if (role != "Dueño" && role != "Gerente")
            return StatusCode(403, new { error = "No tiene permisos para modificar la configuración de comprobantes.", code = "ACCESS_DENIED" });

        // Validar que el request tenga al menos un tipo
        if (request.Tipos is null || request.Tipos.Count == 0)
            return BadRequest(new { error = "Debe enviar al menos un tipo de comprobante.", code = "VALIDATION_ERROR" });

        // Validar invariante: el resultado final debe dejar al menos 1 tipo habilitado.
        // Si ningún tipo en la solicitud tiene Habilitado = true, rechazar preventivamente.
        var alMenosUnoHabilitado = request.Tipos.Any(t => t.Habilitado);
        if (!alMenosUnoHabilitado)
            return BadRequest(new { error = "Debe mantener al menos un tipo de comprobante habilitado.", code = "COMPROBANTE_MIN_ONE_REQUIRED" });

        // Procesar cada tipo: habilitar o deshabilitar según el estado recibido
        foreach (var tipo in request.Tipos)
        {
            var tipoNormalizado = _comprobantesConfigService.NormalizarTipoComprobante(tipo.TipoComprobante);

            if (tipo.Habilitado)
            {
                var result = await _comprobantesConfigService.HabilitarTipoAsync(comercioId.Value, tipoNormalizado);
                if (!result.Success)
                    return BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode });
            }
            else
            {
                var result = await _comprobantesConfigService.DeshabilitarTipoAsync(comercioId.Value, tipoNormalizado);
                if (!result.Success)
                    return BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode });
            }
        }

        // Retornar la configuración actualizada
        var configuracionActualizada = await _comprobantesConfigService.GetHabilitadosAsync(comercioId.Value);
        return Ok(configuracionActualizada);
    }
}
