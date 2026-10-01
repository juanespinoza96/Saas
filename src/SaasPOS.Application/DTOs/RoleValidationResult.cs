namespace SaasPOS.Application.DTOs;

/// <summary>
/// Resultado de la validación de un rol contra el plan del comercio.
/// </summary>
/// <param name="EsValido">Indica si el rol es permitido por el plan.</param>
/// <param name="MensajeError">Mensaje descriptivo cuando el rol no es válido.</param>
/// <param name="RolesDisponibles">Lista de roles permitidos por el plan (útil para mensajes de error).</param>
public record RoleValidationResult(
    bool EsValido,
    string? MensajeError,
    IReadOnlyList<string>? RolesDisponibles);
