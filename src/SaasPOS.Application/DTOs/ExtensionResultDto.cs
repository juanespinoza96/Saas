namespace SaasPOS.Application.DTOs;

/// <summary>
/// DTO de respuesta tras la extensión exitosa de un trial.
/// </summary>
public record ExtensionResultDto(
    DateOnly NuevaFechaProximoCorte
);
