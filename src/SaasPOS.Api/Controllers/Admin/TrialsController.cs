using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Controllers.Admin;

/// <summary>
/// Controller para gestión de pruebas gratuitas (trials) desde el panel SuperAdmin.
/// Permite crear, listar, convertir y extender trials de comercios.
/// </summary>
[ApiController]
[Route("api/admin/trials")]
[Authorize(Policy = "SuperAdminOnly")]
public class TrialsController : ControllerBase
{
    private readonly ITrialService _trialService;

    public TrialsController(ITrialService trialService)
    {
        _trialService = trialService;
    }

    /// <summary>
    /// POST /api/admin/trials — Crea un nuevo comercio con trial de 15 días.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CrearTrial([FromBody] CreateTrialRequest request)
    {
        var superAdminId = GetUsuarioId();

        try
        {
            var resultado = await _trialService.CrearTrialAsync(request, superAdminId);
            return StatusCode(201, resultado);
        }
        catch (InvalidOperationException ex)
        {
            return MapErrorToResponse(ex.Message);
        }
    }

    /// <summary>
    /// GET /api/admin/trials — Obtiene listado de todos los trials (activos y expirados).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ObtenerTrials()
    {
        var trials = await _trialService.ObtenerTrialsAsync();
        return Ok(trials);
    }

    /// <summary>
    /// GET /api/admin/trials/resumen — Obtiene contadores resumen de trials.
    /// </summary>
    [HttpGet("resumen")]
    public async Task<IActionResult> ObtenerResumen()
    {
        var resumen = await _trialService.ObtenerResumenAsync();
        return Ok(resumen);
    }

    /// <summary>
    /// POST /api/admin/trials/{suscripcionId}/convertir — Convierte un trial a suscripción paga.
    /// </summary>
    [HttpPost("{suscripcionId:int}/convertir")]
    public async Task<IActionResult> ConvertirTrial(int suscripcionId, [FromBody] ConvertTrialRequest request)
    {
        var superAdminId = GetUsuarioId();

        try
        {
            var resultado = await _trialService.ConvertirTrialAsync(suscripcionId, request, superAdminId);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return MapErrorToResponse(ex.Message);
        }
    }

    /// <summary>
    /// POST /api/admin/trials/{suscripcionId}/extender — Extiende la duración de un trial activo.
    /// </summary>
    [HttpPost("{suscripcionId:int}/extender")]
    public async Task<IActionResult> ExtenderTrial(int suscripcionId, [FromBody] ExtendTrialRequest request)
    {
        var superAdminId = GetUsuarioId();

        try
        {
            var resultado = await _trialService.ExtenderTrialAsync(suscripcionId, request, superAdminId);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return MapErrorToResponse(ex.Message);
        }
    }

    /// <summary>
    /// Mapea los códigos de error del módulo Trial a respuestas HTTP apropiadas
    /// según la tabla de Error Handling del diseño.
    /// </summary>
    private IActionResult MapErrorToResponse(string errorCode)
    {
        return errorCode switch
        {
            // 409 Conflict
            "RUC_DUPLICATE" => Conflict(new { error = "El RUC ya existe como Comercio.", code = errorCode }),
            "TRIAL_YA_UTILIZADO" => Conflict(new { error = "El RUC ya tiene historial de trial.", code = errorCode }),

            // 400 Bad Request
            "CONVERSION_NO_APLICA" => BadRequest(new { error = "La suscripción no está en estado Trial ni Trial_Expirado.", code = errorCode }),
            "PLAN_INVALIDO" => BadRequest(new { error = "El PlanId especificado no existe.", code = errorCode }),
            "EXTENSION_NO_APLICA" => BadRequest(new { error = "La suscripción no está en estado Trial.", code = errorCode }),
            "DIAS_FUERA_DE_RANGO" => BadRequest(new { error = "Los días deben estar entre 1 y 15.", code = errorCode }),
            "LIMITE_EXTENSION_SUPERADO" => BadRequest(new { error = "El acumulado de extensiones superaría 30 días.", code = errorCode }),

            // 400 Bad Request - Validaciones de input
            "RUC_INVALIDO" => BadRequest(new { error = "El RUC debe contener exactamente 13 dígitos numéricos.", code = errorCode }),
            "RAZON_SOCIAL_INVALIDA" => BadRequest(new { error = "La razón social debe tener entre 1 y 200 caracteres.", code = errorCode }),

            // 503 Service Unavailable
            "VERIFICACION_NO_DISPONIBLE" => StatusCode(503, new { error = "No se pudo validar la elegibilidad del trial.", code = errorCode }),

            // Fallback para errores no mapeados
            _ => StatusCode(500, new { error = "Error interno del servidor.", code = "ERROR_INTERNO" })
        };
    }

    /// <summary>
    /// Extrae el ID del usuario autenticado desde los claims del JWT.
    /// </summary>
    private int GetUsuarioId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return sub is not null ? int.Parse(sub) : 0;
    }
}
