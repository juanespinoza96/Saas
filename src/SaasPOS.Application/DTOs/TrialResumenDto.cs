namespace SaasPOS.Application.DTOs;

/// <summary>
/// DTO con contadores resumen de trials para el panel admin.
/// </summary>
public record TrialResumenDto(
    /// <summary>Total de suscripciones con Estado "Trial".</summary>
    int TotalActivos,
    /// <summary>Trials con Estado "Trial" y 5 o menos días restantes.</summary>
    int PorExpirar,
    /// <summary>Total de suscripciones con Estado "Trial_Expirado".</summary>
    int Expirados
);
