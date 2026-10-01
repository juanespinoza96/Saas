namespace SaasPOS.Application.DTOs.Results;

/// <summary>
/// Resultado de reconsultar la contraseña temporal vigente de una solicitud Aprobada
/// (Requirement 5). Encapsula éxito/fallo, un <see cref="RecoveryErrorCode"/> para el mapeo HTTP
/// y, en caso de éxito, la contraseña temporal descifrada junto con su fecha de expiración.
/// Una temporal expirada o borrada nunca se entrega descifrada (Gone → 410).
/// </summary>
public sealed class TempPasswordResult
{
    /// <summary>Indica si se entregó la contraseña temporal.</summary>
    public bool Success { get; init; }

    /// <summary>Código de error para el mapeo HTTP; <see cref="RecoveryErrorCode.None"/> cuando hay éxito.</summary>
    public RecoveryErrorCode ErrorCode { get; init; }

    /// <summary>Mensaje descriptivo del fallo (nulo cuando hay éxito).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Contraseña temporal descifrada (solo en caso de éxito).</summary>
    public string? TempPassword { get; init; }

    /// <summary>Fecha de expiración de la contraseña temporal (solo en caso de éxito).</summary>
    public DateTime? FechaExpiracion { get; init; }

    /// <summary>Crea un resultado exitoso con la contraseña temporal descifrada y su expiración.</summary>
    public static TempPasswordResult Ok(string tempPassword, DateTime fechaExpiracion) => new()
    {
        Success = true,
        ErrorCode = RecoveryErrorCode.None,
        TempPassword = tempPassword,
        FechaExpiracion = fechaExpiracion,
    };

    /// <summary>Crea un resultado fallido con el código de error y un mensaje opcional.</summary>
    public static TempPasswordResult Fail(RecoveryErrorCode code, string? message = null) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
    };
}
