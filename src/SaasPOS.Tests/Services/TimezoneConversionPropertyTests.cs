using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Tests de propiedades para TimezoneService (conversiones de zona horaria).
/// Valida invariantes universales con generadores aleatorios usando FsCheck.
/// </summary>
public class TimezoneConversionPropertyTests
{
    private readonly TimezoneService _service;

    public TimezoneConversionPropertyTests()
    {
        var loggerMock = new Mock<ILogger<TimezoneService>>();
        _service = new TimezoneService(loggerMock.Object);
    }

    #region Generadores

    /// <summary>
    /// Genera DateTimes UTC aleatorios en el rango 1970-2100, evitando default/Min/Max.
    /// </summary>
    private static Arbitrary<DateTime> ArbitraryUtcDateTime()
    {
        return Arb.From(
            Gen.Choose(1970, 2100).SelectMany(year =>
            Gen.Choose(1, 12).SelectMany(month =>
            Gen.Choose(1, DateTime.DaysInMonth(year, month)).SelectMany(day =>
            Gen.Choose(0, 23).SelectMany(hour =>
            Gen.Choose(0, 59).SelectMany(minute =>
            Gen.Choose(0, 59).Select(second =>
                new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc)
            ))))))
        );
    }

    /// <summary>
    /// Genera un ID de zona horaria IANA válido del sistema.
    /// </summary>
    private static Gen<string> GenValidTimezone()
    {
        var zones = TimeZoneInfo.GetSystemTimeZones()
            .Select(tz => tz.Id)
            .ToArray();

        return Gen.Elements(zones);
    }

    /// <summary>
    /// Genera strings que NO son zonas IANA reconocidas.
    /// </summary>
    private static Arbitrary<string> ArbitraryInvalidTimezone()
    {
        // Strings inválidos conocidos + strings aleatorios alfanuméricos
        var knownInvalid = new[]
        {
            "Invalid/Zone", "XYZABC", "Foo/Bar/Baz", "NotA/Timezone",
            "123/456", "America/NoExiste", "Europe/Inventada",
            "Fake_Zone", "ZZZ", "Nowhere/Land"
        };

        var genKnownInvalid = Gen.Elements(knownInvalid);

        // Generar strings alfanuméricos aleatorios de 3-20 caracteres
        var genRandomAlpha = Gen.Choose(3, 20).SelectMany(len =>
            Gen.ArrayOf(len, Gen.Elements(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_".ToCharArray()
            )).Select(chars => new string(chars))
        );

        // Combinar ambos generadores y filtrar que no sean zonas válidas
        var combined = Gen.OneOf(genKnownInvalid, genRandomAlpha)
            .Where(s => !string.IsNullOrWhiteSpace(s) && !TimeZoneInfo.TryFindSystemTimeZoneById(s, out _));

        return Arb.From(combined);
    }

    #endregion

    #region Property 5: Round-trip UTC → local → UTC preserva el valor original

    /// <summary>
    /// Property 5: Round-trip UTC → local → UTC preserva el valor original.
    /// Para cualquier DateTime UTC no ambiguo y cualquier zona horaria IANA válida,
    /// aplicar ConvertFromUtc seguido de ConvertToUtc produce un valor igual al original
    /// con tolerancia de ±1 milisegundo.
    /// 
    /// **Validates: Requirements 4.1, 4.2, 4.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RoundTrip_UtcToLocalToUtc_PreservaValorOriginal()
    {
        // Combinar generadores: DateTime UTC + zona válida
        var gen = ArbitraryUtcDateTime().Generator.SelectMany(utcDt =>
            GenValidTimezone().Select(zone => (utcDt, zone))
        );

        return Prop.ForAll(Arb.From(gen), tuple =>
        {
            var (utcOriginal, ianaZone) = tuple;

            // Obtener TimeZoneInfo para filtrar tiempos ambiguos en la ida
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(ianaZone, out var tzInfo))
                return true; // Si por alguna razón no se encuentra, skip

            // Convertir UTC → local
            var local = _service.ConvertFromUtc(utcOriginal, ianaZone);

            // Filtrar tiempos ambiguos (fall-back) — el round-trip puede no preservarse
            if (tzInfo.IsAmbiguousTime(local))
                return true; // Skip tiempos ambiguos

            // Convertir local → UTC
            var resultado = _service.ConvertToUtc(local, ianaZone);

            // Verificar que el round-trip preserva el valor original (tolerancia ±1ms)
            var diferenciaMs = Math.Abs((resultado - utcOriginal).TotalMilliseconds);
            return diferenciaMs <= 1;
        });
    }

    #endregion

    #region Property 6: Zona no reconocida retorna valor sin modificar

    /// <summary>
    /// Property 6: Zona no reconocida en servicio de conversión retorna valor sin modificar.
    /// Para cualquier DateTime UTC y cualquier string que no sea una zona IANA reconocida,
    /// el TimezoneService.ConvertFromUtc retorna el mismo DateTime sin modificación.
    /// 
    /// **Validates: Requirements 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ZonaNoReconocida_RetornaValorSinModificar()
    {
        var gen = ArbitraryUtcDateTime().Generator.SelectMany(utcDt =>
            ArbitraryInvalidTimezone().Generator.Select(zone => (utcDt, zone))
        );

        return Prop.ForAll(Arb.From(gen), tuple =>
        {
            var (utcOriginal, zonaInvalida) = tuple;

            // Aplicar ConvertFromUtc con zona inválida
            var resultado = _service.ConvertFromUtc(utcOriginal, zonaInvalida);

            // El resultado debe ser exactamente igual al original
            return resultado == utcOriginal;
        });
    }

    #endregion
}
