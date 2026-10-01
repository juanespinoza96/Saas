using Microsoft.Extensions.Logging;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementación centralizada del servicio de conversión de zona horaria.
/// Usa TimeZoneInfo de .NET 9 con soporte nativo para identificadores IANA.
/// Registrado como Singleton en DI.
/// </summary>
public class TimezoneService : ITimezoneService
{
    private readonly ILogger<TimezoneService> _logger;

    public TimezoneService(ILogger<TimezoneService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Convierte un DateTime UTC a la zona horaria IANA especificada.
    /// Si la zona no es reconocida, retorna el valor sin modificar y logea warning.
    /// Si el DateTime cae en un gap (spring-forward), ajusta al instante posterior válido más cercano.
    /// </remarks>
    public DateTime ConvertFromUtc(DateTime utcDateTime, string ianaTimeZone)
    {
        // Req 4.7: Si DateTime es nulo/inválido, lanzar ArgumentNullException
        if (utcDateTime == default)
            throw new ArgumentNullException(nameof(utcDateTime), "El parámetro de fecha es inválido.");

        // Req 4.3: Si zona no reconocida, retornar valor sin modificar y logear warning
        if (!TryGetTimeZoneInfo(ianaTimeZone, out var timeZoneInfo))
        {
            _logger.LogWarning(
                "Zona horaria no reconocida: '{ZonaHoraria}'. Se retorna el valor UTC sin modificar.",
                ianaTimeZone);
            return utcDateTime;
        }

        // Asegurar que el DateTime tenga Kind = Utc para la conversión
        var utcInput = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        // Realizar la conversión UTC → local
        var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcInput, timeZoneInfo);

        // Req 4.4: Spring-forward — ajustar al instante posterior válido más cercano
        if (timeZoneInfo.IsInvalidTime(localDateTime))
        {
            localDateTime = AjustarSpringForward(localDateTime, timeZoneInfo);
        }

        return localDateTime;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Convierte un DateTime local a UTC usando la zona horaria IANA especificada.
    /// Si la zona no es reconocida, retorna el valor sin modificar y logea warning.
    /// Si el DateTime es ambiguo (fall-back), asume el offset estándar (no DST).
    /// </remarks>
    public DateTime ConvertToUtc(DateTime localDateTime, string ianaTimeZone)
    {
        // Req 4.7: Si DateTime es nulo/inválido, lanzar ArgumentNullException
        if (localDateTime == default)
            throw new ArgumentNullException(nameof(localDateTime), "El parámetro de fecha es inválido.");

        // Req 4.3: Si zona no reconocida, retornar valor sin modificar y logear warning
        if (!TryGetTimeZoneInfo(ianaTimeZone, out var timeZoneInfo))
        {
            _logger.LogWarning(
                "Zona horaria no reconocida: '{ZonaHoraria}'. Se retorna el valor local sin modificar.",
                ianaTimeZone);
            return localDateTime;
        }

        // Req 4.5: Fall-back (ambigüedad) — asumir offset estándar (no DST)
        if (timeZoneInfo.IsAmbiguousTime(localDateTime))
        {
            return ConvertirAmbiguoConOffsetEstandar(localDateTime, timeZoneInfo);
        }

        // Si el tiempo es inválido (spring-forward gap), ajustar antes de convertir
        if (timeZoneInfo.IsInvalidTime(localDateTime))
        {
            var ajustado = AjustarSpringForward(localDateTime, timeZoneInfo);
            var unspecifiedAjustado = DateTime.SpecifyKind(ajustado, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecifiedAjustado, timeZoneInfo);
        }

        // Conversión normal
        var unspecifiedInput = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecifiedInput, timeZoneInfo);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Valida si un identificador IANA es reconocido usando TimeZoneInfo.TryFindSystemTimeZoneById.
    /// .NET 9 soporta identificadores IANA de forma nativa.
    /// </remarks>
    public bool IsValidTimeZone(string ianaTimeZone)
    {
        if (string.IsNullOrWhiteSpace(ianaTimeZone))
            return false;

        return TimeZoneInfo.TryFindSystemTimeZoneById(ianaTimeZone, out _);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Obtiene todas las zonas horarias del sistema y las mapea a TimezoneInfoDto.
    /// El DisplayName se construye con el formato "(UTC±HH:mm) Ciudad/Región".
    /// </remarks>
    public IReadOnlyList<TimezoneInfoDto> GetAvailableTimezones()
    {
        var timezones = TimeZoneInfo.GetSystemTimeZones();

        return timezones
            .Select(tz => new TimezoneInfoDto
            {
                Id = tz.Id,
                DisplayName = FormatDisplayName(tz),
                UtcOffset = FormatUtcOffset(tz.BaseUtcOffset)
            })
            .OrderBy(tz => ParseOffset(tz.UtcOffset))
            .ThenBy(tz => tz.Id, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    #region Métodos privados

    /// <summary>
    /// Intenta obtener el TimeZoneInfo para un identificador IANA.
    /// </summary>
    private static bool TryGetTimeZoneInfo(string ianaTimeZone, out TimeZoneInfo timeZoneInfo)
    {
        timeZoneInfo = null!;

        if (string.IsNullOrWhiteSpace(ianaTimeZone))
            return false;

        return TimeZoneInfo.TryFindSystemTimeZoneById(ianaTimeZone, out timeZoneInfo!);
    }

    /// <summary>
    /// Ajusta un DateTime que cae en un gap de spring-forward al instante posterior válido más cercano.
    /// </summary>
    private static DateTime AjustarSpringForward(DateTime invalidTime, TimeZoneInfo timeZoneInfo)
    {
        // Obtener las reglas de ajuste para encontrar el gap
        var rules = timeZoneInfo.GetAdjustmentRules();

        foreach (var rule in rules)
        {
            // Verificar si la regla aplica al año del tiempo inválido
            if (invalidTime.Year >= rule.DateStart.Year && invalidTime.Year <= rule.DateEnd.Year)
            {
                var transitionStart = GetTransitionDateTime(invalidTime.Year, rule.DaylightTransitionStart);

                // El gap va desde transitionStart hasta transitionStart + daylightDelta
                var gapEnd = transitionStart + rule.DaylightDelta;

                // Si el tiempo cae dentro del gap, ajustar al final del gap
                if (invalidTime >= transitionStart && invalidTime < gapEnd)
                {
                    return gapEnd;
                }
            }
        }

        // Fallback: si no encontramos la regla, avanzar un minuto hasta encontrar un tiempo válido
        var adjusted = invalidTime;
        while (timeZoneInfo.IsInvalidTime(adjusted))
        {
            adjusted = adjusted.AddMinutes(1);
        }
        return adjusted;
    }

    /// <summary>
    /// Convierte un DateTime ambiguo (fall-back) a UTC usando el offset estándar (no DST).
    /// </summary>
    private static DateTime ConvertirAmbiguoConOffsetEstandar(DateTime ambiguousTime, TimeZoneInfo timeZoneInfo)
    {
        // Obtener los offsets posibles durante la ambigüedad
        var offsets = timeZoneInfo.GetAmbiguousTimeOffsets(ambiguousTime);

        // El offset estándar (no DST) es el mayor offset (más cercano a UTC o más alejado de UTC
        // dependiendo del hemisferio, pero en fall-back el estándar es el offset base)
        var baseOffset = timeZoneInfo.BaseUtcOffset;

        // Buscar el offset que corresponde al offset estándar (base, no DST)
        var standardOffset = offsets.FirstOrDefault(o => o == baseOffset);

        // Si no encontramos exactamente el base offset, tomar el que NO es DST
        // En fall-back, el offset estándar es típicamente el mayor de los dos (menos negativo)
        if (standardOffset == default && offsets.Length > 0)
        {
            // El offset estándar en fall-back es el que está más alejado de UTC (menor valor)
            // para zonas con offset negativo, o el mayor para zonas con offset positivo.
            // La convención es: el estándar tiene delta = 0 respecto al BaseUtcOffset.
            standardOffset = offsets.OrderByDescending(o => o).First();
            if (offsets.Any(o => o == baseOffset))
                standardOffset = baseOffset;
        }

        // Si aún no tenemos un offset válido, usar el base
        if (standardOffset == default)
            standardOffset = baseOffset;

        // Calcular UTC: UTC = local - offset
        var utcTicks = ambiguousTime.Ticks - standardOffset.Ticks;
        return new DateTime(utcTicks, DateTimeKind.Utc);
    }

    /// <summary>
    /// Calcula la fecha/hora exacta de una transición DST para un año específico.
    /// </summary>
    private static DateTime GetTransitionDateTime(int year, TimeZoneInfo.TransitionTime transition)
    {
        if (transition.IsFixedDateRule)
        {
            return new DateTime(year, transition.Month, transition.Day,
                transition.TimeOfDay.Hour, transition.TimeOfDay.Minute, transition.TimeOfDay.Second);
        }

        // Regla flotante: calcular el día de la semana N del mes
        var firstDayOfMonth = new DateTime(year, transition.Month, 1);
        var dayOfWeek = transition.DayOfWeek;
        var week = transition.Week;

        // Encontrar el primer día de la semana deseado en el mes
        var daysUntilTarget = ((int)dayOfWeek - (int)firstDayOfMonth.DayOfWeek + 7) % 7;
        var firstOccurrence = firstDayOfMonth.AddDays(daysUntilTarget);

        // Avanzar a la N-ésima ocurrencia
        var targetDate = firstOccurrence.AddDays((week - 1) * 7);

        // Si week == 5, significa "último" — retroceder si nos pasamos del mes
        if (targetDate.Month != transition.Month)
            targetDate = targetDate.AddDays(-7);

        return new DateTime(targetDate.Year, targetDate.Month, targetDate.Day,
            transition.TimeOfDay.Hour, transition.TimeOfDay.Minute, transition.TimeOfDay.Second);
    }

    /// <summary>
    /// Formatea el nombre descriptivo de la zona horaria: "(UTC±HH:mm) Ciudad".
    /// Extrae el último segmento del ID IANA como nombre de ciudad/región.
    /// </summary>
    private static string FormatDisplayName(TimeZoneInfo tz)
    {
        var offset = FormatUtcOffset(tz.BaseUtcOffset);
        var cityName = ExtractCityName(tz.Id);
        return $"(UTC{offset}) {cityName}";
    }

    /// <summary>
    /// Extrae el nombre de la ciudad/región del último segmento del ID IANA.
    /// Ejemplo: "America/Guayaquil" → "Guayaquil", "America/Argentina/Buenos_Aires" → "Buenos_Aires"
    /// </summary>
    private static string ExtractCityName(string ianaId)
    {
        if (string.IsNullOrEmpty(ianaId))
            return ianaId;

        var lastSlash = ianaId.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < ianaId.Length - 1)
            return ianaId[(lastSlash + 1)..].Replace('_', ' ');

        return ianaId;
    }

    /// <summary>
    /// Formatea un TimeSpan de offset a formato "±HH:mm".
    /// </summary>
    private static string FormatUtcOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absOffset = offset.Duration();
        return $"{sign}{absOffset.Hours:D2}:{absOffset.Minutes:D2}";
    }

    /// <summary>
    /// Parsea un string de offset "±HH:mm" a un valor numérico para ordenamiento.
    /// </summary>
    private static double ParseOffset(string offsetStr)
    {
        if (string.IsNullOrEmpty(offsetStr))
            return 0;

        var sign = offsetStr[0] == '-' ? -1.0 : 1.0;
        var parts = offsetStr[1..].Split(':');

        if (parts.Length == 2
            && int.TryParse(parts[0], out var hours)
            && int.TryParse(parts[1], out var minutes))
        {
            return sign * (hours + minutes / 60.0);
        }

        return 0;
    }

    #endregion
}
