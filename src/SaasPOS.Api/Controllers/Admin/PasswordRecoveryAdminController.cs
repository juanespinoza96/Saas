using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.DTOs.Results;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Controllers.Admin;

/// <summary>
/// Controlador cross-tenant para que el SuperAdmin gestione las solicitudes de recuperación de
/// contraseña de usuarios con rol Dueño de cualquier comercio (Requirements 3.3, 3.8, 13.1, 13.2).
///
/// El TenantContextMiddleware establece AdminTenantContext para las rutas /api/admin/, lo que
/// desactiva los filtros de tenant; adicionalmente la lógica de <see cref="IAuthService"/> usa
/// IgnoreQueryFilters() para resolver solicitudes/usuarios de cualquier comercio de forma segura.
///
/// Reutiliza los mismos métodos de <see cref="IAuthService"/> que el AuthController del POS: el
/// servicio ya aplica la matriz de autorización que restringe al SuperAdmin a operar únicamente
/// sobre Dueños, por lo que aquí solo se resuelve el identificador del aprobador desde el claim
/// (sub / NameIdentifier) y se mapea el resultado al código HTTP correspondiente. Sigue el patrón
/// del <see cref="LogsAuditoriaController"/>.
/// </summary>
[ApiController]
[Route("api/admin/password-recovery")]
[Authorize(Policy = "SuperAdminOnly")]
public class PasswordRecoveryAdminController : ControllerBase
{
    private readonly IAuthService _authService;

    public PasswordRecoveryAdminController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// GET /api/admin/password-recovery/pending — Lista las solicitudes de recuperación de usuarios
    /// con rol Dueño de cualquier comercio (Req 13.1). El servicio devuelve la variante Admin del DTO
    /// (PendingRecoveryAdminDto) con ComercioId/ComercioNombre para mostrar la columna de comercio
    /// en el modal (Req 13.2). Devuelve 200 con el arreglo de solicitudes; 401 si el claim no es válido.
    /// </summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        if (!TryGetApproverUserId(out var approverUserId))
            return Unauthorized();

        // El servicio restringe al SuperAdmin a solicitudes de Dueños cross-tenant y enriquece el DTO.
        var pending = await _authService.GetPendingRecoveriesAsync(approverUserId);

        return Ok(pending);
    }

    /// <summary>
    /// POST /api/admin/password-recovery/approve/{requestId} — Aprueba una solicitud de un Dueño
    /// (Req 3.3, 3.8, 13.1). El servicio valida que el solicitante sea Dueño; en caso de éxito
    /// devuelve la contraseña temporal en texto plano y su fecha de expiración (Req 4.6).
    /// Mapea 200 (éxito), 403 (no autorizado / no es Dueño), 404 (no existe), 409 (ya resuelta/vencida).
    /// </summary>
    [HttpPost("approve/{requestId:int}")]
    public async Task<IActionResult> Approve(int requestId)
    {
        if (!TryGetApproverUserId(out var approverUserId))
            return Unauthorized();

        var result = await _authService.ApprovePasswordRecoveryAsync(requestId, approverUserId);

        if (result.Success)
        {
            return Ok(new ApproveResponse
            {
                TempPassword = result.TempPassword ?? string.Empty,
                FechaExpiracion = result.FechaExpiracion ?? default,
            });
        }

        // Mapeo del ErrorCode del ApprovalResult al código HTTP correspondiente
        // (403 jerarquía/comercio/no-Dueño, 404 no existe, 409 ya resuelta/vencida).
        return result.ErrorCode switch
        {
            RecoveryErrorCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new { message = result.ErrorMessage ?? "No autorizado para aprobar esta solicitud." }),
            RecoveryErrorCode.NotFound => NotFound(
                new { message = result.ErrorMessage ?? "La solicitud de recuperación no existe." }),
            RecoveryErrorCode.Conflict => Conflict(
                new { message = result.ErrorMessage ?? "La solicitud ya fue resuelta o venció." }),
            _ => StatusCode(StatusCodes.Status403Forbidden,
                new { message = result.ErrorMessage ?? "No autorizado para aprobar esta solicitud." }),
        };
    }

    /// <summary>
    /// POST /api/admin/password-recovery/reject/{requestId} — Rechaza una solicitud de un Dueño con
    /// un motivo obligatorio (Req 15.2, 15.3). El servicio revalida el motivo, la jerarquía y que el
    /// solicitante sea Dueño. Mapea 200 (éxito), 400 (motivo vacío/faltante), 403 (no autorizado),
    /// 404 (no existe).
    /// </summary>
    [HttpPost("reject/{requestId:int}")]
    public async Task<IActionResult> Reject(int requestId, [FromBody] RejectRecoveryRequest request)
    {
        if (!TryGetApproverUserId(out var approverUserId))
            return Unauthorized();

        var result = await _authService.RejectPasswordRecoveryAsync(requestId, approverUserId, request.MotivoRechazo);

        if (result.Success)
            return Ok(new { message = "La solicitud de recuperación fue rechazada." });

        // Mapeo del ErrorCode del RejectResult al código HTTP correspondiente:
        // 400 motivo faltante, 403 jerarquía/no-Dueño, 404 no existe.
        return result.ErrorCode switch
        {
            RecoveryErrorCode.MissingReason => BadRequest(
                new { message = result.ErrorMessage ?? "El motivo de rechazo es obligatorio." }),
            RecoveryErrorCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new { message = result.ErrorMessage ?? "No autorizado para rechazar esta solicitud." }),
            RecoveryErrorCode.NotFound => NotFound(
                new { message = result.ErrorMessage ?? "La solicitud de recuperación no existe." }),
            _ => StatusCode(StatusCodes.Status403Forbidden,
                new { message = result.ErrorMessage ?? "No autorizado para rechazar esta solicitud." }),
        };
    }

    /// <summary>
    /// GET /api/admin/password-recovery/{requestId}/temp-password — Reconsulta ilimitada de la
    /// contraseña temporal vigente de una solicitud Aprobada de un Dueño (Req 5.1, 5.2, 5.4). El
    /// servicio valida la autorización cross-tenant y la vigencia con validación perezosa.
    /// Mapea 200 (éxito), 403 (no autorizado), 404 (no existe), 410 (expiró o fue borrada).
    /// </summary>
    [HttpGet("{requestId:int}/temp-password")]
    public async Task<IActionResult> GetTempPassword(int requestId)
    {
        if (!TryGetApproverUserId(out var approverUserId))
            return Unauthorized();

        var result = await _authService.GetTempPasswordAsync(requestId, approverUserId);

        if (result.Success)
        {
            return Ok(new ApproveResponse
            {
                TempPassword = result.TempPassword ?? string.Empty,
                FechaExpiracion = result.FechaExpiracion ?? default,
            });
        }

        // Mapeo del ErrorCode del TempPasswordResult al código HTTP correspondiente:
        // 403 no autorizado, 404 no existe, 410 si expiró o fue borrada (Gone).
        return result.ErrorCode switch
        {
            RecoveryErrorCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new { message = result.ErrorMessage ?? "No autorizado para consultar esta contraseña temporal." }),
            RecoveryErrorCode.NotFound => NotFound(
                new { message = result.ErrorMessage ?? "La solicitud de recuperación no existe." }),
            RecoveryErrorCode.Gone => StatusCode(StatusCodes.Status410Gone,
                new { message = result.ErrorMessage ?? "La contraseña temporal expiró o ya no está disponible." }),
            _ => StatusCode(StatusCodes.Status403Forbidden,
                new { message = result.ErrorMessage ?? "No autorizado para consultar esta contraseña temporal." }),
        };
    }

    /// <summary>
    /// Resuelve el identificador del SuperAdmin aprobador a partir del claim del JWT
    /// (NameIdentifier o sub). Devuelve false si el claim no está presente o no es un entero válido.
    /// </summary>
    private bool TryGetApproverUserId(out int approverUserId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        return int.TryParse(userIdClaim, out approverUserId);
    }
}
