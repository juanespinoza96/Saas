namespace SaasPOS.Application.DTOs;

/// <summary>
/// DTO de respuesta tras la creación exitosa de un trial.
/// </summary>
public record TrialResultDto(
    int ComercioId,
    int SuscripcionId,
    string RazonSocial,
    DateOnly FechaInicio,
    DateOnly FechaProximoCorte
);
