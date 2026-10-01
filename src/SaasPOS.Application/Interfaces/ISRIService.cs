namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Service for electronic invoice (Factura Electrónica) operations with Ecuador's SRI.
/// Handles XML construction, signing, submission, and status management.
/// </summary>
public interface ISRIService
{
    /// <summary>
    /// Emits an electronic invoice for a given sale.
    /// Validates plan/feature access, builds SRI XML, signs it, and sends to SRI.
    /// Updates Ventas.EstadoSRI based on SRI response.
    /// Idempotent: returns immediately if EstadoSRI is already 'Autorizada'.
    /// </summary>
    Task<SRIResult> EmitirFacturaElectronicaAsync(int ventaId, int comercioId);

    /// <summary>
    /// Queries the current authorization status of an invoice by its ClaveAcceso.
    /// </summary>
    Task<SRIEstado> ConsultarEstadoAsync(string claveAcceso);
}

/// <summary>
/// Result of an electronic invoice emission attempt.
/// </summary>
public record SRIResult(
    bool Success,
    string? ClaveAcceso,
    string Estado,
    string? MensajeError);

/// <summary>
/// State returned from SRI status query.
/// </summary>
public record SRIEstado(
    string ClaveAcceso,
    string Estado,
    string? NumeroAutorizacion,
    DateTime? FechaAutorizacion,
    string? MensajeError);
