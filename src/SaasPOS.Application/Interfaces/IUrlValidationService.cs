namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Servicio de validación de URLs para protección contra Server-Side Request Forgery (SSRF).
/// Valida que las URLs de callbacks/webhooks pertenezcan a dominios permitidos
/// y rechaza URLs que apunten a direcciones IP privadas o internas.
/// </summary>
public interface IUrlValidationService
{
    /// <summary>
    /// Valida si una URL es segura para realizar callbacks/webhooks.
    /// Verifica que el dominio esté en la lista blanca y que no apunte a IPs privadas/internas.
    /// </summary>
    /// <param name="url">La URL a validar.</param>
    /// <returns>True si la URL es válida y segura; false en caso contrario.</returns>
    bool IsUrlAllowed(string url);

    /// <summary>
    /// Valida si una URL es segura, retornando el motivo del rechazo si no lo es.
    /// </summary>
    /// <param name="url">La URL a validar.</param>
    /// <returns>Tupla con (esValida, motivoRechazo). Si es válida, motivoRechazo es null.</returns>
    (bool IsValid, string? RejectionReason) ValidateUrl(string url);
}
