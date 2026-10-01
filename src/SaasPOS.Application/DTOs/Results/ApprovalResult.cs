namespace SaasPOS.Application.DTOs.Results;

/// <summary>
/// Resultado de aprobar una SolicitudRecuperacion (Requirements 3, 4).
/// Encapsula éxito/fallo, un <see cref="RecoveryErrorCode"/> para el mapeo HTTP y,
/// en caso de éxito, la contraseña temporal en texto plano junto con su fecha de expiración
/// para que el aprobador pueda comunicarla al solicitante.
/// </summary>
public sealed class ApprovalResult
{
    /// <summary>Indica si la aprobación fue exitosa.</summary>
    public bool Success { get; init; }

    /// <summary>Código de error para el mapeo HTTP; <see cref="RecoveryErrorCode.None"/> cuando hay éxito.</summary>
    public RecoveryErrorCode ErrorCode { get; init; }

    /// <summary>Mensaje descriptivo del fallo (nulo cuando hay éxito).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Contraseña temporal en texto plano generada al aprobar (solo en caso de éxito).</summary>
    public string? TempPassword { get; init; }

    /// <summary>Fecha de expiración de la contraseña temporal (solo en caso de éxito).</summary>
    public DateTime? FechaExpiracion { get; init; }

    /// <summary>Crea un resultado exitoso con la contraseña temporal y su expiración.</summary>
    public static ApprovalResult Ok(string tempPassword, DateTime fechaExpiracion) => new()
    {
        Success = true,
        ErrorCode = RecoveryErrorCode.None,
        TempPassword = tempPassword,
        FechaExpiracion = fechaExpiracion,
    };

    /// <summary>Crea un resultado fallido con el código de error y un mensaje opcional.</summary>
    public static ApprovalResult Fail(RecoveryErrorCode code, string? message = null) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
    };
}
