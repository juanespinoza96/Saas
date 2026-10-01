namespace SaasPOS.Infrastructure.Configuration;

/// <summary>
/// Configuración de seguridad para protección SSRF y Content Security Policy.
/// Se carga desde la sección "Security" de appsettings.json.
/// </summary>
public class SecuritySettings
{
    public const string SectionName = "Security";

    /// <summary>
    /// Lista blanca de dominios permitidos para URLs de callbacks y webhooks (protección SSRF).
    /// Solo se aceptan URLs cuyo host pertenezca a esta lista.
    /// </summary>
    public List<string> AllowedCallbackDomains { get; set; } = [];

    /// <summary>
    /// Dominios de confianza adicionales para la Content Security Policy (script-src, connect-src).
    /// Se agregan al 'self' por defecto.
    /// </summary>
    public List<string> TrustedScriptDomains { get; set; } = [];

    /// <summary>
    /// Dominios de confianza para conexiones (connect-src) además de 'self'.
    /// </summary>
    public List<string> TrustedConnectDomains { get; set; } = [];
}
