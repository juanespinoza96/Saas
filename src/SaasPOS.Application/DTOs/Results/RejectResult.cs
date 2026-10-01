namespace SaasPOS.Application.DTOs.Results;

/// <summary>
/// Resultado de rechazar una SolicitudRecuperacion (Requirement 15).
/// Encapsula éxito/fallo y un <see cref="RecoveryErrorCode"/> para el mapeo HTTP
/// (por ejemplo, motivo faltante → 400, jerarquía/comercio → 403, inexistente → 404).
/// </summary>
public sealed class RejectResult
{
    /// <summary>Indica si el rechazo fue exitoso.</summary>
    public bool Success { get; init; }

    /// <summary>Código de error para el mapeo HTTP; <see cref="RecoveryErrorCode.None"/> cuando hay éxito.</summary>
    public RecoveryErrorCode ErrorCode { get; init; }

    /// <summary>Mensaje descriptivo del fallo (nulo cuando hay éxito).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Crea un resultado exitoso de rechazo.</summary>
    public static RejectResult Ok() => new()
    {
        Success = true,
        ErrorCode = RecoveryErrorCode.None,
    };

    /// <summary>Crea un resultado fallido con el código de error y un mensaje opcional.</summary>
    public static RejectResult Fail(RecoveryErrorCode code, string? message = null) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
    };
}
