namespace SaasPOS.Application.DTOs;

/// <summary>
/// DTO de respuesta tras la conversión exitosa de un trial a suscripción paga.
/// </summary>
public record ConversionResultDto(
    string NombrePlan,
    decimal MontoCuota,
    DateOnly FechaProximoCorte
);
