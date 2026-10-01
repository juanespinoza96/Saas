using SaasPOS.Application.DTOs;
using SaasPOS.Application.DTOs.Results;

namespace SaasPOS.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password);
    Task<bool> InvalidateSessionAsync(int userId);
    Task<bool> InvalidateAllSessionsForComercioAsync(int comercioId);

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
