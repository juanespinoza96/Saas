namespace SaasPOS.Application.DTOs.Results;

/// <summary>
/// Código de error compartido por los resultados del flujo de recuperación de contraseña
/// jerárquica. Permite que la capa API mapee cada fallo al código HTTP correcto sin acoplar
/// la lógica de servicio a detalles de transporte.
/// </summary>
public enum RecoveryErrorCode
{
    /// <summary>Sin error: la operación fue exitosa.</summary>
    None = 0,

    /// <summary>Jerarquía/comercio no permitidos o auto-aprobación (mapea a HTTP 403).</summary>
    Forbidden,

    /// <summary>Credenciales incorrectas, p. ej. contraseña actual inválida (mapea a HTTP 401).</summary>
    InvalidCredentials,

    /// <summary>La contraseña incumple la Politica_Password estricta (mapea a HTTP 400).</summary>
    PolicyError,

    /// <summary>Falta el motivo de rechazo obligatorio (mapea a HTTP 400).</summary>
    MissingReason,

    /// <summary>La solicitud o el recurso no existe (mapea a HTTP 404).</summary>
    NotFound,

    /// <summary>La solicitud ya está resuelta o vencida al intentar aprobarla (mapea a HTTP 409).</summary>
    Conflict,

    /// <summary>La contraseña temporal expiró o fue borrada, ya no se entrega (mapea a HTTP 410).</summary>
    Gone,
}
