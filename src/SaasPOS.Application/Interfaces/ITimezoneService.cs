using SaasPOS.Application.DTOs;

namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Servicio centralizado de conversión de zona horaria.
/// Todas las conversiones del sistema pasan por esta interfaz.
/// </summary>
public interface ITimezoneService
{
    /// <summary>
    /// Convierte un DateTime UTC a la zona horaria especificada.
    /// Preserva precisión hasta milisegundos.
    /// </summary>
    /// <param name="utcDateTime">Fecha y hora en UTC.</param>
    /// <param name="ianaTimeZone">Identificador IANA de la zona horaria destino.</param>
    /// <returns>DateTime convertido a la zona horaria especificada.</returns>
    DateTime ConvertFromUtc(DateTime utcDateTime, string ianaTimeZone);

    /// <summary>
    /// Convierte un DateTime local a UTC usando la zona horaria especificada.
    /// Preserva precisión hasta milisegundos.
    /// </summary>
    /// <param name="localDateTime">Fecha y hora en la zona horaria local.</param>
    /// <param name="ianaTimeZone">Identificador IANA de la zona horaria origen.</param>
    /// <returns>DateTime convertido a UTC.</returns>
    DateTime ConvertToUtc(DateTime localDateTime, string ianaTimeZone);

    /// <summary>
    /// Valida si un identificador IANA es reconocido por el sistema.
    /// </summary>
    /// <param name="ianaTimeZone">Identificador IANA a validar.</param>
    /// <returns>True si la zona horaria es válida; false en caso contrario.</returns>
    bool IsValidTimeZone(string ianaTimeZone);

    /// <summary>
    /// Obtiene la lista completa de zonas horarias disponibles.
    /// </summary>
    /// <returns>Lista de zonas horarias con su información descriptiva.</returns>
    IReadOnlyList<TimezoneInfoDto> GetAvailableTimezones();
}
