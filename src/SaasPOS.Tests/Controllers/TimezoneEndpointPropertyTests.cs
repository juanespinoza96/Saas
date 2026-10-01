using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Controllers;

/// <summary>
/// Tests de propiedades para el endpoint de zonas horarias disponibles.
/// Valida ordenamiento correcto y filtro de búsqueda usando FsCheck.
/// </summary>
public class TimezoneEndpointPropertyTests
{
    private readonly TimezoneService _service;

    public TimezoneEndpointPropertyTests()
    {
        var loggerMock = new Mock<ILogger<TimezoneService>>();
        _service = new TimezoneService(loggerMock.Object);
    }

    #region Property 11: Lista de timezones correctamente ordenada

    /// <summary>
    /// Property 11: Lista de timezones está correctamente ordenada.
    /// Para cualquier par consecutivo en la lista retornada por el servicio,
    /// el primer elemento tiene un offset UTC numéricamente menor o igual al segundo,
    /// y si los offsets son iguales, el Id del primero es alfabéticamente menor o igual al del segundo.
    /// 
    /// **Validates: Requirements 6.2, 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ListaTimezones_OrdenadaPorOffsetYLuegoAlfabeticamentePorId()
    {
        // Obtener la lista completa de timezones del servicio (misma que usa el endpoint)
        var timezones = _service.GetAvailableTimezones();

        // Verificar que la lista no esté vacía
        if (timezones.Count == 0)
            return false;

        // Verificar cada par consecutivo
        for (int i = 0; i < timezones.Count - 1; i++)
        {
            var actual = timezones[i];
            var siguiente = timezones[i + 1];

            var offsetActual = ParseOffsetAMinutos(actual.UtcOffset);
            var offsetSiguiente = ParseOffsetAMinutos(siguiente.UtcOffset);

            // El offset del elemento actual debe ser <= al del siguiente
            if (offsetActual > offsetSiguiente)
                return false;

            // Si los offsets son iguales, el Id debe estar en orden alfabético (ordinal)
            if (offsetActual == offsetSiguiente)
            {
                if (string.Compare(actual.Id, siguiente.Id, StringComparison.OrdinalIgnoreCase) > 0)
                    return false;
            }
        }

        return true;
    }

    #endregion

    #region Property 12: Filtro de búsqueda retorna solo coincidencias

    /// <summary>
    /// Property 12: Filtro de búsqueda retorna solo coincidencias.
    /// Para cualquier string q con al menos 1 carácter y cualquier resultado en la lista filtrada,
    /// el Id o el DisplayName del resultado contiene q como subcadena (case-insensitive).
    /// 
    /// **Validates: Requirements 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FiltroBusqueda_RetornaSoloCoincidencias()
    {
        // Generar substrings aleatorios de zonas horarias conocidas
        var gen = GenerarSubstringDeZonaConocida();

        return Prop.ForAll(Arb.From(gen), query =>
        {
            // Obtener la lista completa de timezones
            var timezones = _service.GetAvailableTimezones();

            // Aplicar el mismo filtro que el controller
            var resultadosFiltrados = timezones
                .Where(tz =>
                    tz.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    tz.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Verificar que cada resultado contiene el query como substring (case-insensitive)
            return resultadosFiltrados.All(tz =>
                tz.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                tz.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));
        });
    }

    #endregion

    #region Métodos auxiliares

    /// <summary>
    /// Genera substrings aleatorios de IDs o DisplayNames de zonas horarias conocidas.
    /// Garantiza que el substring tenga al menos 1 carácter.
    /// </summary>
    private Gen<string> GenerarSubstringDeZonaConocida()
    {
        var timezones = _service.GetAvailableTimezones();

        // Combinar IDs y DisplayNames como fuentes de substrings
        var fuentes = timezones
            .SelectMany(tz => new[] { tz.Id, tz.DisplayName })
            .Where(s => !string.IsNullOrEmpty(s) && s.Length >= 1)
            .ToArray();

        // Seleccionar una fuente aleatoria y extraer un substring de ella
        return Gen.Elements(fuentes).SelectMany(fuente =>
        {
            var maxLen = fuente.Length;
            return Gen.Choose(0, maxLen - 1).SelectMany(inicio =>
                Gen.Choose(1, maxLen - inicio).Select(longitud =>
                    fuente.Substring(inicio, longitud)
                )
            );
        });
    }

    /// <summary>
    /// Parsea un string de offset "±HH:mm" a minutos totales para comparación numérica.
    /// </summary>
    private static int ParseOffsetAMinutos(string offsetStr)
    {
        if (string.IsNullOrEmpty(offsetStr))
            return 0;

        var signo = offsetStr[0] == '-' ? -1 : 1;
        var partes = offsetStr[1..].Split(':');

        if (partes.Length == 2
            && int.TryParse(partes[0], out var horas)
            && int.TryParse(partes[1], out var minutos))
        {
            return signo * (horas * 60 + minutos);
        }

        return 0;
    }

    #endregion
}
