using Microsoft.Extensions.Options;
using SaasPOS.Infrastructure.Configuration;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Agrega headers de seguridad a cada respuesta HTTP (Req 23.4, 23.5, 3.2).
/// Incluye Content Security Policy que restringe scripts al mismo origen y dominios de confianza.
/// Debe ubicarse muy temprano en el pipeline (justo después de GlobalExceptionMiddleware).
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _cspHeader;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecuritySettings> securitySettings)
    {
        _next = next;
        _cspHeader = BuildCspHeader(securitySettings.Value);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Prevenir MIME-type sniffing
            headers["X-Content-Type-Options"] = "nosniff";

            // Prevenir clickjacking
            headers["X-Frame-Options"] = "DENY";

            // Forzar HTTPS con HSTS (1 año, incluir subdominios)
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

            // Deshabilitar filtro XSS legacy (práctica moderna: confiar en CSP)
            headers["X-XSS-Protection"] = "0";

            // Controlar información de referrer enviada con las solicitudes
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Restringir funcionalidades del navegador
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // Content Security Policy — Req 3.2: restringir scripts a mismo origen y dominios de confianza
            headers["Content-Security-Policy"] = _cspHeader;

            return Task.CompletedTask;
        });

        await _next(context);
    }

    /// <summary>
    /// Construye el header Content-Security-Policy basado en la configuración de seguridad.
    /// Restringe scripts exclusivamente al mismo origen y dominios de confianza configurados.
    /// </summary>
    private static string BuildCspHeader(SecuritySettings settings)
    {
        // Construir script-src: 'self' + dominios de confianza configurados
        var scriptSources = new List<string> { "'self'" };
        if (settings.TrustedScriptDomains.Count > 0)
        {
            scriptSources.AddRange(settings.TrustedScriptDomains
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim()));
        }

        // Construir connect-src: 'self' + dominios de conexión de confianza
        var connectSources = new List<string> { "'self'" };
        if (settings.TrustedConnectDomains.Count > 0)
        {
            connectSources.AddRange(settings.TrustedConnectDomains
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim()));
        }

        var scriptSrc = string.Join(" ", scriptSources);
        var connectSrc = string.Join(" ", connectSources);

        // CSP completo con directivas restrictivas
        return $"default-src 'self'; " +
               $"script-src {scriptSrc}; " +
               $"style-src 'self' 'unsafe-inline'; " +
               $"img-src 'self' data:; " +
               $"font-src 'self'; " +
               $"connect-src {connectSrc}; " +
               $"frame-ancestors 'none'; " +
               $"base-uri 'self'; " +
               $"form-action 'self'";
    }
}
