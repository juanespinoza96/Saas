using System.Security.Claims;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Middleware que resuelve la zona horaria efectiva para el request actual.
/// Extrae el header X-Timezone, obtiene UsuarioId y ComercioId del JWT,
/// invoca la cascada de resolución y almacena el resultado en HttpContext.Items.
/// </summary>
public class TimezoneResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TimezoneResolutionMiddleware> _logger;

    /// <summary>
    /// Longitud máxima permitida para el valor del header X-Timezone.
    /// </summary>
    private const int MaxTimezoneHeaderLength = 64;

    /// <summary>
    /// Clave utilizada para almacenar la zona horaria resuelta en HttpContext.Items.
    /// </summary>
    private const string ResolvedTimezoneKey = "ResolvedTimezone";

    /// <summary>
    /// Valor por defecto cuando la resolución falla o no hay zona disponible.
    /// </summary>
    private const string DefaultTimezone = "UTC";

    public TimezoneResolutionMiddleware(RequestDelegate next, ILogger<TimezoneResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Extraer el valor del header X-Timezone (limitado a 64 caracteres)
            string? headerTimezone = ExtractTimezoneHeader(context);

            // Obtener UsuarioId y ComercioId de los claims del JWT
            int? usuarioId = GetClaimAsInt(context.User, "sub");
            int? comercioId = GetClaimAsInt(context.User, "comercio_id");

            // Resolver la zona horaria efectiva usando la cascada de prioridad
            var resolver = context.RequestServices.GetRequiredService<ITimezoneResolver>();
            string resolvedTimezone = await resolver.ResolveAsync(usuarioId, comercioId, headerTimezone);

            // Almacenar el resultado en HttpContext.Items para uso posterior
            context.Items[ResolvedTimezoneKey] = resolvedTimezone;
        }
        catch (Exception ex)
        {
            // Fail-safe: si algo falla, usar UTC y continuar sin romper el request
            _logger.LogWarning(ex, "Error al resolver zona horaria. Se usará UTC como valor por defecto.");
            context.Items[ResolvedTimezoneKey] = DefaultTimezone;
        }

        await _next(context);
    }

    /// <summary>
    /// Extrae el valor del header X-Timezone del request.
    /// Si el valor excede 64 caracteres, se ignora y retorna null.
    /// </summary>
    private static string? ExtractTimezoneHeader(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("X-Timezone", out var values))
            return null;

        string? headerValue = values.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(headerValue))
            return null;

        // Si excede la longitud máxima, ignorar el valor
        if (headerValue.Length > MaxTimezoneHeaderLength)
            return null;

        return headerValue;
    }

    /// <summary>
    /// Obtiene un claim del usuario autenticado y lo convierte a int.
    /// Retorna null si el claim no existe o no es un entero válido.
    /// </summary>
    private static int? GetClaimAsInt(ClaimsPrincipal? user, string claimType)
    {
        if (user == null)
            return null;

        string? claimValue = user.FindFirstValue(claimType);

        if (string.IsNullOrEmpty(claimValue))
            return null;

        if (int.TryParse(claimValue, out int result))
            return result;

        return null;
    }
}
