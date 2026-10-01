using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Controllers.Tenants;

/// <summary>
/// Controller para consultar las zonas horarias disponibles en el sistema.
/// No requiere autenticación (AllowAnonymous).
/// </summary>
[ApiController]
[Route("api/tenants/timezones")]
[AllowAnonymous]
public class TimezonesController : ControllerBase
{
    private readonly ITimezoneService _timezoneService;

    public TimezonesController(ITimezoneService timezoneService)
    {
        _timezoneService = timezoneService;
    }

    /// <summary>
    /// GET /api/tenants/timezones — Obtiene la lista de zonas horarias disponibles.
    /// Soporta filtro opcional por query param 'q' (case-insensitive substring match en id y displayName).
    /// Ordena por offset UTC numérico ascendente, luego alfabéticamente por ID IANA.
    /// </summary>
    [HttpGet]
    public IActionResult GetAll([FromQuery] string? q = null)
    {
        var timezones = _timezoneService.GetAvailableTimezones();

        IEnumerable<TimezoneInfoDto> result = timezones;

        // Filtrar por query param 'q' si se proporciona (≥1 carácter)
        if (!string.IsNullOrEmpty(q))
        {
            result = result.Where(tz =>
                tz.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                tz.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // Ordenar por offset UTC numérico (menor a mayor), luego alfabéticamente por ID IANA
        var ordered = result
            .OrderBy(tz => ParseUtcOffset(tz.UtcOffset))
            .ThenBy(tz => tz.Id, StringComparer.Ordinal)
            .ToList();

        return Ok(ordered);
    }

    /// <summary>
    /// Convierte un string de offset UTC (ej: "-05:00", "+03:30") a minutos totales
    /// para permitir ordenamiento numérico.
    /// </summary>
    private static int ParseUtcOffset(string utcOffset)
    {
        if (string.IsNullOrWhiteSpace(utcOffset))
            return 0;

        // Determinar signo
        var sign = 1;
        var offset = utcOffset.AsSpan();

        if (offset[0] == '-')
        {
            sign = -1;
            offset = offset[1..];
        }
        else if (offset[0] == '+')
        {
            offset = offset[1..];
        }

        // Parsear horas y minutos (formato HH:mm)
        var colonIndex = offset.IndexOf(':');
        if (colonIndex < 0)
            return 0;

        if (int.TryParse(offset[..colonIndex], out var hours) &&
            int.TryParse(offset[(colonIndex + 1)..], out var minutes))
        {
            return sign * (hours * 60 + minutes);
        }

        return 0;
    }
}
