using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Tests de propiedad para zona horaria por defecto.
/// Feature: pdf-export-personalizable, Property 1: Zona horaria por defecto para comercios sin configuración
/// **Validates: Requirements 1.2**
/// </summary>
public class PdfExport_Property1_ZonaHorariaDefectoTests
{
    private const string OffsetEcuador = "-05:00";

    /// <summary>
    /// Property A: Para cualquier DateTime UTC, si la zona horaria es null,
    /// ConvertirAZonaHorariaLocal SHALL usar "America/Guayaquil" como fallback,
    /// produciendo offset "-05:00".
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ZonaHorariaNula_UsaFallbackEcuador()
    {
        return Prop.ForAll(ArbitraryFechaUtc(), fechaUtc =>
        {
            var (fechaLocal, offsetTexto) = PdfExportService.ConvertirAZonaHorariaLocal(fechaUtc, null);

            var offsetCorrecto = offsetTexto == OffsetEcuador;
            var diferenciaCorrecta = (fechaLocal - fechaUtc).TotalHours == -5.0;

            return (offsetCorrecto && diferenciaCorrecta)
                .Label($"ZonaHoraria=null => offset={offsetTexto}, diferencia={(fechaLocal - fechaUtc).TotalHours}h");
        });
    }

    /// <summary>
    /// Property B: Para cualquier DateTime UTC, si la zona horaria es string vacío,
    /// ConvertirAZonaHorariaLocal SHALL usar "America/Guayaquil" como fallback,
    /// produciendo offset "-05:00".
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ZonaHorariaVacia_UsaFallbackEcuador()
    {
        return Prop.ForAll(ArbitraryFechaUtc(), fechaUtc =>
        {
            var (fechaLocal, offsetTexto) = PdfExportService.ConvertirAZonaHorariaLocal(fechaUtc, "");

            var offsetCorrecto = offsetTexto == OffsetEcuador;
            var diferenciaCorrecta = (fechaLocal - fechaUtc).TotalHours == -5.0;

            return (offsetCorrecto && diferenciaCorrecta)
                .Label($"ZonaHoraria=\"\" => offset={offsetTexto}, diferencia={(fechaLocal - fechaUtc).TotalHours}h");
        });
    }

    /// <summary>
    /// Property C: Para cualquier DateTime UTC, si la zona horaria es whitespace,
    /// ConvertirAZonaHorariaLocal SHALL usar "America/Guayaquil" como fallback,
    /// produciendo offset "-05:00".
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ZonaHorariaWhitespace_UsaFallbackEcuador()
    {
        // Generar strings de whitespace variado (espacios, tabs, combinaciones)
        var whitespaceGen = Gen.Elements(" ", "  ", "\t", "  \t  ", "\t\t", "   ");
        var arbitraryWhitespace = Arb.From(whitespaceGen);

        return Prop.ForAll(ArbitraryFechaUtc(), arbitraryWhitespace, (fechaUtc, whitespace) =>
        {
            var (fechaLocal, offsetTexto) = PdfExportService.ConvertirAZonaHorariaLocal(fechaUtc, whitespace);

            var offsetCorrecto = offsetTexto == OffsetEcuador;
            var diferenciaCorrecta = (fechaLocal - fechaUtc).TotalHours == -5.0;

            return (offsetCorrecto && diferenciaCorrecta)
                .Label($"ZonaHoraria=\"{whitespace}\" => offset={offsetTexto}, diferencia={(fechaLocal - fechaUtc).TotalHours}h");
        });
    }

    /// <summary>
    /// Property D: Para cualquier DateTime UTC con zona horaria válida "America/Guayaquil",
    /// la conversión SHALL ser consistente con TimeZoneInfo directamente.
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ZonaHorariaExplicitaGuayaquil_ConsistenteConTimeZoneInfo()
    {
        return Prop.ForAll(ArbitraryFechaUtc(), fechaUtc =>
        {
            var (fechaLocal, offsetTexto) = PdfExportService.ConvertirAZonaHorariaLocal(fechaUtc, "America/Guayaquil");

            // Verificar contra conversión directa usando TimeZoneInfo
            var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Guayaquil");
            var fechaEsperada = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(fechaUtc, DateTimeKind.Utc), tz);

            var fechaCoincide = fechaLocal == fechaEsperada;
            var offsetCorrecto = offsetTexto == OffsetEcuador;

            return (fechaCoincide && offsetCorrecto)
                .Label($"Explícita 'America/Guayaquil': fecha={fechaCoincide}, offset={offsetTexto}");
        });
    }

    /// <summary>
    /// Generador de fechas UTC arbitrarias dentro de un rango razonable (2020-2030).
    /// </summary>
    private static Arbitrary<DateTime> ArbitraryFechaUtc()
    {
        var gen = from year in Gen.Choose(2020, 2030)
                  from month in Gen.Choose(1, 12)
                  from day in Gen.Choose(1, 28) // Evitar problemas con meses de menos días
                  from hour in Gen.Choose(0, 23)
                  from minute in Gen.Choose(0, 59)
                  from second in Gen.Choose(0, 59)
                  select new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);

        return Arb.From(gen);
    }
}
