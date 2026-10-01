namespace SaasPOS.Application.DTOs;

/// <summary>
/// DTO para representar un trial en el listado del panel admin.
/// </summary>
public record TrialDto(
    int SuscripcionId,
    int ComercioId,
    string RazonSocial,
    string Ruc,
    DateOnly FechaInicio,
    DateOnly FechaProximoCorte,
    int DiasRestantes,
    string Estado
);
