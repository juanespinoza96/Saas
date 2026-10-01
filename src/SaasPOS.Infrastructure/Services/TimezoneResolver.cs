using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Resuelve la zona horaria efectiva para el request actual aplicando la cascada de prioridad:
/// 1) Encabezado X-Timezone (si válido)
/// 2) Usuario.ZonaHoraria (si no nulo/vacío y válido)
/// 3) Comercio.ZonaHorariaDefecto (si no nulo/vacío y válido)
/// 4) "UTC" como fallback final
/// </summary>
public class TimezoneResolver : ITimezoneResolver
{
    private readonly AppDbContext _dbContext;
    private readonly ITimezoneService _timezoneService;
    private readonly ILogger<TimezoneResolver> _logger;

    public TimezoneResolver(
        AppDbContext dbContext,
        ITimezoneService timezoneService,
        ILogger<TimezoneResolver> logger)
    {
        _dbContext = dbContext;
        _timezoneService = timezoneService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ResolveAsync(int? usuarioId, int? comercioId, string? headerTimezone)
    {
        // ── 1) Encabezado X-Timezone: primer nivel de prioridad ──────────────
        if (!string.IsNullOrWhiteSpace(headerTimezone))
        {
            if (_timezoneService.IsValidTimeZone(headerTimezone))
            {
                return headerTimezone;
            }

            // Header presente pero con valor IANA inválido — logear warning y continuar cascada
            _logger.LogWarning(
                "Encabezado X-Timezone contiene valor IANA inválido: '{HeaderTimezone}'. Se omite y continúa con la cascada.",
                headerTimezone);
        }

        // ── 2) Usuario.ZonaHoraria: segundo nivel de prioridad ───────────────
        if (usuarioId.HasValue)
        {
            var zonaUsuario = await _dbContext.Usuarios
                .Where(u => u.Id == usuarioId.Value)
                .Select(u => u.ZonaHoraria)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(zonaUsuario))
            {
                if (_timezoneService.IsValidTimeZone(zonaUsuario))
                {
                    return zonaUsuario;
                }

                // Zona del usuario almacenada pero inválida — logear warning
                _logger.LogWarning(
                    "Usuario {UsuarioId} tiene ZonaHoraria almacenada con valor IANA inválido: '{ZonaHoraria}'. Se omite y continúa con la cascada.",
                    usuarioId.Value, zonaUsuario);
            }
        }

        // ── 3) Comercio.ZonaHorariaDefecto: tercer nivel de prioridad ────────
        if (comercioId.HasValue)
        {
            var zonaComercio = await _dbContext.Comercios
                .Where(c => c.Id == comercioId.Value)
                .Select(c => c.ZonaHorariaDefecto)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(zonaComercio))
            {
                if (_timezoneService.IsValidTimeZone(zonaComercio))
                {
                    return zonaComercio;
                }

                // Zona del comercio almacenada pero inválida — logear warning
                _logger.LogWarning(
                    "Comercio {ComercioId} tiene ZonaHorariaDefecto con valor IANA inválido: '{ZonaHoraria}'. Se omite y continúa con la cascada.",
                    comercioId.Value, zonaComercio);
            }
        }

        // ── 4) Fallback final: UTC ──────────────────────────────────────────
        return "UTC";
    }
}
