namespace SaasPOS.Application.DTOs;

/// <summary>
/// DTO para solicitar la extensión de un trial activo.
/// </summary>
public record ExtendTrialRequest(
    /// <summary>Cantidad de días adicionales a extender, entre 1 y 15 inclusive.</summary>
    int DiasAdicionales
);
