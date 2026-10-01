namespace SaasPOS.Application.DTOs.Results;

/// <summary>
/// Resultado de un cambio de contraseña, tanto obligatorio (Requirement 10) como
/// voluntario (Requirement 17). Encapsula éxito/fallo y un <see cref="RecoveryErrorCode"/>
/// para el mapeo HTTP (política incumplida → 400, contraseña actual incorrecta → 401,
/// usuario/solicitud inexistente → 404).
/// </summary>
public sealed class ChangePasswordResult
{
    /// <summary>Indica si el cambio de contraseña fue exitoso.</summary>
    public bool Success { get; init; }

    /// <summary>Código de error para el mapeo HTTP; <see cref="RecoveryErrorCode.None"/> cuando hay éxito.</summary>
    public RecoveryErrorCode ErrorCode { get; init; }

    /// <summary>Mensaje descriptivo del fallo (nulo cuando hay éxito).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Crea un resultado exitoso de cambio de contraseña.</summary>
    public static ChangePasswordResult Ok() => new()
    {
        Success = true,
        ErrorCode = RecoveryErrorCode.None,
    };

    /// <summary>Crea un resultado fallido con el código de error y un mensaje opcional.</summary>
    public static ChangePasswordResult Fail(RecoveryErrorCode code, string? message = null) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
    };
}
