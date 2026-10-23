using SaasPOS.Application.DTOs;
using SaasPOS.Application.DTOs.Results;

namespace SaasPOS.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password);
    Task<bool> InvalidateSessionAsync(int userId);
    Task<bool> InvalidateAllSessionsForComercioAsync(int comercioId);

    // ── Renovación silenciosa de sesión (sliding expiration) ──

    /// <summary>
    /// Renueva el access token de una sesión viva re-emitiendo el JWT con un nuevo <c>jti</c>/<c>iat</c>
    /// pero conservando el <c>login_at</c> original, salvo que se supere el tope absoluto del rol
    /// (Requirements 1.3, 1.4, 1.6). Devuelve <see cref="RefreshResult.Ok"/> con el token renovado si la
    /// sesión sigue dentro del tope, o <see cref="RefreshResult.Fail"/> cuando lo supera (el controlador
    /// lo traduce a 401). No persiste nada: el tope se mide con <paramref name="loginAtUnix"/> en UTC.
    /// La firma NO incluye el <c>iat</c> presentado porque el re-chequeo del corte masivo vive en el
    /// controlador (AuthController), no en el servicio.
    /// </summary>
    Task<RefreshResult> RefreshTokenAsync(int userId, int comercioId, string role, int? sucursalId, bool mustChangePassword, long loginAtUnix);

    // ── Recuperación de contraseña jerárquica ──

    /// <summary>
    /// Crea una solicitud de recuperación de forma silenciosa (Requirements 1, 2).
    /// Siempre devuelve true para no revelar la existencia del correo.
    /// </summary>
    Task<bool> RequestPasswordRecoveryAsync(string email);

    /// <summary>
    /// Aprueba una solicitud aplicando jerarquía, comercio y auto-aprobación (Requirements 3, 4, 15).
    /// Devuelve la contraseña temporal generada en caso de éxito.
    /// </summary>
    Task<ApprovalResult> ApprovePasswordRecoveryAsync(int requestId, int approverUserId);

    /// <summary>
    /// Rechaza una solicitud con un motivo obligatorio (Requirement 15).
    /// </summary>
    Task<RejectResult> RejectPasswordRecoveryAsync(int requestId, int approverUserId, string motivoRechazo);

    /// <summary>
    /// Lista las solicitudes que el aprobador puede gestionar según jerarquía y comercio
    /// (Requirements 3, 5). Calcula minutos restantes y vigencia de la temporal.
    /// </summary>
    Task<IReadOnlyList<PendingRecoveryDto>> GetPendingRecoveriesAsync(int approverUserId);

    /// <summary>
    /// Reconsulta la contraseña temporal vigente de una solicitud Aprobada (Requirement 5).
    /// No entrega la temporal si expiró o fue borrada.
    /// </summary>
    Task<TempPasswordResult> GetTempPasswordAsync(int requestId, int approverUserId);

    // ── Cambio de contraseña ──

    /// <summary>
    /// Cambio de contraseña obligatorio tras usar una temporal (Requirement 10).
    /// Desactiva la Bandera_Cambio y borra la temporal asociada.
    /// </summary>
    Task<ChangePasswordResult> ChangePasswordAsync(int userId, string newPassword);

    /// <summary>
    /// Cambio de contraseña voluntario desde Configuración (Requirement 17).
    /// Verifica la contraseña actual y no altera la Bandera_Cambio ni la temporal.
    /// </summary>
    Task<ChangePasswordResult> ChangePasswordVoluntaryAsync(int userId, string currentPassword, string newPassword);
}
