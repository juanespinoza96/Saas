namespace SaasPOS.Application.DTOs;

/// <summary>
/// DTO para solicitar la creación de un comercio con prueba gratuita de 15 días.
/// </summary>
public record CreateTrialRequest(
    /// <summary>Exactamente 13 dígitos numéricos (RUC ecuatoriano).</summary>
    string Ruc,
    /// <summary>Razón social del comercio, entre 1 y 200 caracteres.</summary>
    string RazonSocial
);
