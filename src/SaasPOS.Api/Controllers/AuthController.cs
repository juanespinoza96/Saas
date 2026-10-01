using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.DTOs.Results;
using SaasPOS.Application.Interfaces;
using SaasPOS.Api.Middleware;

namespace SaasPOS.Api.Controllers;

[ApiController]
[Route("api/tenants/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IJtiBlocklist _jtiBlocklist;
    private readonly IAccountLockoutService _accountLockout;
    private readonly ILoginAttemptTracker _loginAttemptTracker;
    private readonly IAuditService _auditService;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly ILogger<AuthController> _logger;

    // Constantes de la LoginPolicy (Req 4.5, 4.6, 4.7)
    private const int MaxLoginAttempts = 3;
    private const int LockoutSeconds = 1800; // 30 minutos

    public AuthController(
        IAuthService authService,
        IJtiBlocklist jtiBlocklist,
        IAccountLockoutService accountLockout,
        ILoginAttemptTracker loginAttemptTracker,
        IAuditService auditService,
        ISecurityAuditService securityAuditService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _jtiBlocklist = jtiBlocklist;
        _accountLockout = accountLockout;
        _loginAttemptTracker = loginAttemptTracker;
        _auditService = auditService;
        _securityAuditService = securityAuditService;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a user and returns a JWT token.
    /// Returns a generic error message for any failure mode (Req 3.2).
    /// Enforces account lockout after 10 failed attempts in 30 min (Req 23.8).
    /// Includes remainingAttempts and lockoutSeconds on credential failures (Req 4.5, 4.6, 4.7).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var ipAddress = RateLimitHelpers.ObtenerIpCliente(HttpContext);

        // Req 23.8: Check lockout before attempting authentication.
        // Return same generic message to not reveal lockout state.
        if (_accountLockout.IsLockedOut(request.Email))
        {
            _ = LogFailedLoginAsync(request.Email, ipAddress, "Cuenta bloqueada por intentos fallidos");
            return Unauthorized(new { message = "Credenciales inválidas" });
        }

        var result = await _authService.LoginAsync(request.Email, request.Password);

        if (!result.Success)
        {
            // Si el usuario está inactivo, no contar como intento fallido
            // y retornar un mensaje claro sin revelar existencia a atacantes
            if (result.ErrorCode == "USER_INACTIVE")
            {
                _ = LogFailedLoginAsync(request.Email, ipAddress, "Usuario inactivo");
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = result.ErrorMessage,
                    code = "USER_INACTIVE"
                });
            }

            _accountLockout.RecordFailedAttempt(request.Email);

            // Req 4.5, 4.6, 4.7: Rastrear intentos fallidos por IP+email
            var currentAttempts = _loginAttemptTracker.RecordFailedAttempt(ipAddress, request.Email);
            var remainingAttempts = MaxLoginAttempts - currentAttempts;

            _ = LogFailedLoginAsync(request.Email, ipAddress, "Credenciales inválidas");

            // Req 4.7: Tercer intento fallido → HTTP 429 con retryAfterSeconds
            if (currentAttempts >= MaxLoginAttempts)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = "Credenciales inválidas",
                    remainingAttempts = 0,
                    lockoutSeconds = LockoutSeconds,
                    retryAfterSeconds = LockoutSeconds
                });
            }

            // Req 4.5, 4.6: Incluir remainingAttempts y lockoutSeconds
            return Unauthorized(new
            {
                message = "Credenciales inválidas",
                remainingAttempts = remainingAttempts,
                lockoutSeconds = LockoutSeconds
            });
        }

        _accountLockout.ResetAttempts(request.Email);
        _loginAttemptTracker.ResetAttempts(ipAddress, request.Email);
        return Ok(new { token = result.Token });
    }

    /// <summary>
    /// Solicita la recuperación de contraseña. Responde SIEMPRE 200 sin importar si el correo
    /// existe en el sistema (flujo silencioso, Req 1.1). Está protegido por la política de rate
    /// limiting existente (3 solicitudes/hora por IP → 429 al excederse, Req 14.1).
    /// </summary>
    [HttpPost("password-recovery/request")]
    [AllowAnonymous]
    [EnableRateLimiting("PasswordRecoveryPolicy")]
    public async Task<IActionResult> RequestPasswordRecovery([FromBody] PasswordRecoveryRequest request)
    {
        // Invoca el servicio; la creación silenciosa y el bloqueo de duplicados se resuelven allí.
        await _authService.RequestPasswordRecoveryAsync(request.Email);

        // Siempre 200 — nunca se revela si el correo corresponde a un usuario existente.
        return Ok(new { message = "Si el correo existe, se procesará la solicitud." });
    }

    /// <summary>
    /// Aprueba una solicitud de recuperación de contraseña. Requiere un Dueño o Gerente
    /// autenticado (Req 3.6); las restricciones de jerarquía y comercio se validan en el
    /// servicio. Al aprobar, devuelve la contraseña temporal en texto plano y su expiración
    /// para que el aprobador la comunique al solicitante (Req 4.6).
    /// </summary>
    [HttpPost("password-recovery/approve/{requestId:int}")]
    [Authorize(Roles = "Dueño,Gerente")]
    public async Task<IActionResult> ApprovePasswordRecovery(int requestId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        if (!int.TryParse(userIdClaim, out var approverUserId))
            return Unauthorized();

        var result = await _authService.ApprovePasswordRecoveryAsync(requestId, approverUserId);

        if (result.Success)
        {
            // Éxito: 200 con la contraseña temporal y su fecha de expiración (Req 4.6)
            return Ok(new ApproveResponse
            {
                TempPassword = result.TempPassword ?? string.Empty,
                FechaExpiracion = result.FechaExpiracion ?? default,
            });
        }

        // Mapeo del ErrorCode del ApprovalResult al código HTTP correspondiente
        // (Req 3.4 → 403 jerarquía/comercio/auto-aprobación, 404 no existe, 409 ya resuelta/vencida).
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
    /// Lista las solicitudes de recuperación que el aprobador puede gestionar dentro de su
    /// comercio (Req 5.3). Solo Dueño o Gerente autenticados (Req 3.6); el servicio filtra por
    /// mismo ComercioId y rol estrictamente inferior. Devuelve 200 con el arreglo de solicitudes;
    /// si el claim del aprobador no es válido, 401.
    /// </summary>
    [HttpGet("password-recovery/pending")]
    [Authorize(Roles = "Dueño,Gerente")]
    public async Task<IActionResult> GetPendingRecoveries()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        if (!int.TryParse(userIdClaim, out var approverUserId))
            return Unauthorized();

        // El servicio aplica el aislamiento por comercio y la jerarquía; devuelve la lista ya filtrada.
        var pending = await _authService.GetPendingRecoveriesAsync(approverUserId);

        return Ok(pending);
    }

    /// <summary>
    /// Reconsulta ilimitada de la contraseña temporal vigente de una solicitud Aprobada
    /// (Req 5.1, 5.2, 5.4). Solo Dueño o Gerente autenticados; el servicio valida jerarquía y
    /// comercio. Mapea el resultado a 200 con la temporal y su expiración, o al código de error
    /// correspondiente. Una temporal expirada o borrada nunca se entrega descifrada (410).
    /// </summary>
    [HttpGet("password-recovery/{requestId:int}/temp-password")]
    [Authorize(Roles = "Dueño,Gerente")]
    public async Task<IActionResult> GetTempPassword(int requestId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        if (!int.TryParse(userIdClaim, out var approverUserId))
            return Unauthorized();

        var result = await _authService.GetTempPasswordAsync(requestId, approverUserId);

        if (result.Success)
        {
            // Éxito: 200 con la contraseña temporal descifrada y su fecha de expiración (Req 5.1)
            return Ok(new ApproveResponse
            {
                TempPassword = result.TempPassword ?? string.Empty,
                FechaExpiracion = result.FechaExpiracion ?? default,
            });
        }

        // Mapeo del ErrorCode del TempPasswordResult al código HTTP correspondiente:
        // 403 no autorizado (jerarquía/comercio), 404 no existe, 410 si expiró o fue borrada (Gone).
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
    /// Rechaza una solicitud de recuperación con un motivo obligatorio (Req 15.2, 15.3).
    /// Solo Dueño o Gerente autenticados; el servicio valida el motivo, la jerarquía y el comercio.
    /// Mapea el resultado a 200, 400 (motivo vacío/faltante), 403 (jerarquía/comercio) o 404 (no existe).
    /// </summary>
    [HttpPost("password-recovery/reject/{requestId:int}")]
    [Authorize(Roles = "Dueño,Gerente")]
    public async Task<IActionResult> RejectPasswordRecovery(int requestId, [FromBody] RejectRecoveryRequest request)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        if (!int.TryParse(userIdClaim, out var approverUserId))
            return Unauthorized();

        // El servicio vuelve a validar que el motivo no esté vacío (MissingReason → 400).
        var result = await _authService.RejectPasswordRecoveryAsync(requestId, approverUserId, request.MotivoRechazo);

        if (result.Success)
            return Ok(new { message = "La solicitud de recuperación fue rechazada." });

        // Mapeo del ErrorCode del RejectResult al código HTTP correspondiente:
        // 400 motivo faltante, 403 jerarquía/comercio, 404 no existe.
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
    /// Cambio de contraseña obligatorio tras usar una contraseña temporal (Req 9.3, 10.1).
    /// Requiere un usuario autenticado; el identificador se toma del claim (sub/NameIdentifier),
    /// nunca del cuerpo. La nueva contraseña se valida contra la Politica_Password estricta en el
    /// servicio. Mapea el resultado a 200 (éxito) o 400 (incumple política, PolicyError, Req 11.1).
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        if (!int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var result = await _authService.ChangePasswordAsync(userId, request.NewPassword);

        if (result.Success)
            return Ok(new { message = "La contraseña se actualizó correctamente." });

        // Mapeo del ErrorCode del ChangePasswordResult al código HTTP correspondiente:
        // 400 si la nueva contraseña incumple la política (PolicyError), 404 si el usuario no existe.
        return result.ErrorCode switch
        {
            RecoveryErrorCode.PolicyError => BadRequest(
                new { message = result.ErrorMessage ?? "La nueva contraseña no cumple la política de seguridad." }),
            RecoveryErrorCode.NotFound => NotFound(
                new { message = result.ErrorMessage ?? "El usuario no existe." }),
            _ => BadRequest(
                new { message = result.ErrorMessage ?? "No se pudo cambiar la contraseña." }),
        };
    }

    /// <summary>
    /// Cambio de contraseña voluntario desde Configuración (Req 17.1, 17.3, 17.5). Requiere un
    /// usuario autenticado; el identificador se toma del claim (sub/NameIdentifier). Verifica la
    /// contraseña actual con BCrypt y valida la nueva contra la Politica_Password estricta.
    /// Mapea el resultado a 200 (éxito), 401 (contraseña actual incorrecta) o 400 (nueva incumple política).
    /// </summary>
    [HttpPost("change-password-voluntary")]
    [Authorize]
    public async Task<IActionResult> ChangePasswordVoluntary([FromBody] ChangePasswordVoluntaryRequest request)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        if (!int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var result = await _authService.ChangePasswordVoluntaryAsync(userId, request.CurrentPassword, request.NewPassword);

        if (result.Success)
            return Ok(new { message = "La contraseña se actualizó correctamente." });

        // Mapeo del ErrorCode del ChangePasswordResult al código HTTP correspondiente:
        // 401 si la contraseña actual es incorrecta (InvalidCredentials), 400 si la nueva incumple
        // la política (PolicyError), 404 si el usuario no existe.
        return result.ErrorCode switch
        {
            RecoveryErrorCode.InvalidCredentials => Unauthorized(
                new { message = result.ErrorMessage ?? "La contraseña actual es incorrecta." }),
            RecoveryErrorCode.PolicyError => BadRequest(
                new { message = result.ErrorMessage ?? "La nueva contraseña no cumple la política de seguridad." }),
            RecoveryErrorCode.NotFound => NotFound(
                new { message = result.ErrorMessage ?? "El usuario no existe." }),
            _ => BadRequest(
                new { message = result.ErrorMessage ?? "No se pudo cambiar la contraseña." }),
        };
    }

    /// <summary>
    /// Logs out the current user by adding their token's JTI to the blocklist (Req 3.3, 3.4).
    /// Any subsequent request with the same token will receive 401.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var expClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);

        if (string.IsNullOrEmpty(jti) || string.IsNullOrEmpty(expClaim))
            return BadRequest(new { message = "Token inválido para logout." });

        if (!long.TryParse(expClaim, out var expUnix))
            return BadRequest(new { message = "Token inválido para logout." });

        var expiration = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
        _jtiBlocklist.AddToBlocklist(jti, expiration);

        return Ok(new { message = "Sesión cerrada exitosamente." });
    }

    /// <summary>
    /// Logs a failed login attempt to the audit trail (Req 3.4, 23.10).
    /// Utiliza tanto IAuditService como ISecurityAuditService para máxima cobertura.
    /// </summary>
    private async Task LogFailedLoginAsync(string email, string ipAddress, string reason)
    {
        try
        {
            // Registrar en auditoría de seguridad especializada (Req 3.4)
            await _securityAuditService.LogAuthenticationFailedAsync(email, ipAddress, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log security audit for failed login from {Email}", email);
        }
    }
}
