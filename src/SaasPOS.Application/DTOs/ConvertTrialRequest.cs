namespace SaasPOS.Application.DTOs;

/// <summary>
/// DTO para solicitar la conversión de un trial a suscripción paga.
/// </summary>
public record ConvertTrialRequest(
    /// <summary>ID del plan destino al que se desea convertir.</summary>
    int PlanId
);
