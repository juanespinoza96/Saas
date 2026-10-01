namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Resuelve la zona horaria efectiva para el request actual
/// según la cascada de prioridad definida.
/// </summary>
public interface ITimezoneResolver
{
    /// <summary>
    /// Resuelve la zona horaria efectiva para el usuario y request actuales.
    /// Prioridad: X-Timezone header > Usuario.ZonaHoraria > Comercio.ZonaHorariaDefecto > "UTC"
    /// </summary>
    /// <param name="usuarioId">ID del usuario autenticado, o null si no está disponible.</param>
    /// <param name="comercioId">ID del comercio del tenant actual, o null si no está disponible.</param>
    /// <param name="headerTimezone">Valor del encabezado X-Timezone del request, o null si no fue enviado.</param>
    /// <returns>Identificador IANA de la zona horaria resuelta. Nunca retorna null.</returns>
    Task<string> ResolveAsync(int? usuarioId, int? comercioId, string? headerTimezone);
}
