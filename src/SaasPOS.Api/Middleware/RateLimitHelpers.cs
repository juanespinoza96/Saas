using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Métodos auxiliares estáticos para la lógica de rate limiting diferenciado.
/// Proporcionan extracción de IP del cliente y validación/extracción de UserId del JWT.
/// </summary>
public static class RateLimitHelpers
{
    /// <summary>
    /// Extrae la dirección IP real del cliente.
    /// Soporta el header X-Forwarded-For (toma el primer valor de la cadena)
    /// para escenarios con proxy inverso o balanceador de carga.
    /// Si no hay IP disponible, retorna "unknown" como clave de partición.
    /// </summary>
    /// <param name="context">El contexto HTTP de la petición actual.</param>
    /// <returns>La dirección IP del cliente como string.</returns>
    public static string ObtenerIpCliente(HttpContext context)
    {
        // Verificar si existe el header X-Forwarded-For (proxy/balanceador)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Tomar la primera IP de la cadena (IP original del cliente)
            return forwardedFor.Split(',')[0].Trim();
        }

        // Fallback: usar la IP de conexión directa
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Valida el token JWT del header Authorization y extrae el claim "sub" (UserId).
    /// Utiliza validación HMAC-SHA256, verificando Issuer y Audience configurados.
    /// Si el token es inválido, expirado, malformado o no contiene el claim "sub",
    /// retorna null para clasificar la petición como tráfico anónimo.
    /// </summary>
    /// <param name="context">El contexto HTTP de la petición actual.</param>
    /// <param name="parameters">Parámetros de validación del token (clave, Issuer, Audience).</param>
    /// <returns>El UserId extraído del claim "sub", o null si el token no es válido.</returns>
    public static string? ExtraerUserIdDeToken(HttpContext context, TokenValidationParameters parameters)
    {
        // Verificar que exista el header Authorization con esquema Bearer
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        // Extraer el token (todo después de "Bearer ")
        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, parameters, out _);

            // Extraer el claim "sub" que contiene el UserId
            var userIdClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirst("sub");

            // Si no hay claim "sub", clasificar como anónimo
            return userIdClaim?.Value;
        }
        catch
        {
            // Token inválido (firma incorrecta, expirado, malformado, etc.)
            // → clasificar como tráfico anónimo
            return null;
        }
    }

    /// <summary>
    /// Extrae el campo "email" del body JSON de la petición.
    /// Habilita buffering para permitir que otros middlewares también lean el body.
    /// Retorna el email en minúsculas, o "unknown" si el body no es JSON válido
    /// o no contiene el campo "email".
    /// </summary>
    /// <param name="context">El contexto HTTP de la petición actual.</param>
    /// <returns>El email extraído en minúsculas, o "unknown" si no se puede obtener.</returns>
    public static string ExtraerEmailDeBody(HttpContext context)
    {
        // Habilitar buffering para permitir múltiples lecturas del body
        context.Request.EnableBuffering();
        try
        {
            // Posicionar el stream al inicio antes de leer
            context.Request.Body.Position = 0;
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = reader.ReadToEndAsync().GetAwaiter().GetResult();

            // Restaurar posición del body stream para que otros middlewares puedan leerlo
            context.Request.Body.Position = 0;

            // Parsear JSON y extraer el campo "email"
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("email", out var emailProp))
                return emailProp.GetString()?.ToLowerInvariant() ?? "unknown";
        }
        catch
        {
            // Body no es JSON válido o error de lectura → usar valor por defecto
        }
        return "unknown";
    }
}
