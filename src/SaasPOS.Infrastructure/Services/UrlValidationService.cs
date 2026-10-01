using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Configuration;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementación de validación de URLs para protección SSRF (Req 3.1).
/// Valida contra lista blanca de dominios y rechaza IPs privadas/internas.
/// </summary>
public class UrlValidationService : IUrlValidationService
{
    private readonly HashSet<string> _allowedDomains;

    public UrlValidationService(IOptions<SecuritySettings> settings)
    {
        // Normalizar dominios a minúsculas para comparación case-insensitive
        _allowedDomains = settings.Value.AllowedCallbackDomains
            .Select(d => d.Trim().ToLowerInvariant())
            .Where(d => !string.IsNullOrEmpty(d))
            .ToHashSet();
    }

    /// <inheritdoc />
    public bool IsUrlAllowed(string url)
    {
        var (isValid, _) = ValidateUrl(url);
        return isValid;
    }

    /// <inheritdoc />
    public (bool IsValid, string? RejectionReason) ValidateUrl(string url)
    {
        // Validar que la URL no esté vacía
        if (string.IsNullOrWhiteSpace(url))
            return (false, "La URL no puede estar vacía.");

        // Intentar parsear la URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, "La URL no tiene un formato válido.");

        // Solo permitir esquemas HTTP/HTTPS
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return (false, "Solo se permiten URLs con esquema HTTP o HTTPS.");

        var host = uri.Host.ToLowerInvariant();

        // Rechazar si el host es una dirección IP privada o interna
        if (IsPrivateOrInternalHost(host))
            return (false, "No se permiten URLs que apunten a direcciones IP privadas o internas.");

        // Verificar que el dominio esté en la lista blanca
        if (!IsDomainAllowed(host))
            return (false, $"El dominio '{host}' no está en la lista de dominios permitidos.");

        return (true, null);
    }

    /// <summary>
    /// Verifica si el host corresponde a una dirección IP privada o interna.
    /// Incluye: 127.x.x.x, 10.x.x.x, 192.168.x.x, 172.16-31.x.x, ::1, localhost, etc.
    /// </summary>
    private static bool IsPrivateOrInternalHost(string host)
    {
        // Rechazar localhost explícitamente
        if (host == "localhost" || host == "localhost.localdomain")
            return true;

        // Intentar parsear como dirección IP
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return IsPrivateIpAddress(ipAddress);
        }

        // Rechazar dominios que resuelven a hosts internos comunes
        if (host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".localhost"))
            return true;

        return false;
    }

    /// <summary>
    /// Determina si una dirección IP es privada, loopback o reservada.
    /// </summary>
    private static bool IsPrivateIpAddress(IPAddress ip)
    {
        // IPv6 loopback (::1)
        if (IPAddress.IsLoopback(ip))
            return true;

        // IPv4 mapped to IPv6 — extraer la dirección IPv4
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // 127.0.0.0/8 — Loopback
            if (bytes[0] == 127)
                return true;

            // 10.0.0.0/8 — Privada clase A
            if (bytes[0] == 10)
                return true;

            // 172.16.0.0/12 — Privada clase B (172.16.x.x a 172.31.x.x)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            // 192.168.0.0/16 — Privada clase C
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;

            // 169.254.0.0/16 — Link-local
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;

            // 0.0.0.0/8 — Red actual
            if (bytes[0] == 0)
                return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fe80::/10 — Link-local IPv6
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                return true;

            // fc00::/7 — Unique Local Address (ULA)
            if ((bytes[0] & 0xfe) == 0xfc)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Verifica si el dominio está en la lista blanca.
    /// Soporta coincidencia exacta y subdominios (ej: "api.ejemplo.com" coincide con "ejemplo.com").
    /// </summary>
    private bool IsDomainAllowed(string host)
    {
        // Si no hay dominios configurados, rechazar todo
        if (_allowedDomains.Count == 0)
            return false;

        // Coincidencia exacta
        if (_allowedDomains.Contains(host))
            return true;

        // Coincidencia por subdominio: verificar si el host termina en ".dominio"
        foreach (var allowed in _allowedDomains)
        {
            if (host.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
