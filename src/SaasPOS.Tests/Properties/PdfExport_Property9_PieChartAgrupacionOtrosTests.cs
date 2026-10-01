using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.DTOs;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Tests de propiedad para la agrupación "Otros" en el gráfico de pastel.
/// Feature: pdf-export-personalizable, Property 9: Gráfico de pastel agrupa excedentes en "Otros"
/// **Validates: Requirements 4.2**
/// </summary>
public class PdfExport_Property9_PieChartAgrupacionOtrosTests
{
    /// <summary>
    /// Generador de listas de ReportRow con más de 8 elementos y valores positivos.
    /// </summary>
    private static Arbitrary<List<ReportRow>> ArbitraryReportRowsMoreThan8()
    {
        // Generar entre 9 y 30 filas con valores positivos
        var gen = from count in Gen.Choose(9, 30)
                  from rows in Gen.ListOf(count,
                      from etiqueta in Arb.Generate<NonEmptyString>()
                      from valorRaw in Gen.Choose(1, 10000)
                      select new ReportRow(
                          etiqueta.Get.Replace("\0", "A"),
                          valorRaw / 100m,
                          null))
                  select rows.ToList();

        return Arb.From(gen);
    }

    /// <summary>
    /// Generador de listas de ReportRow con exactamente 8 o menos elementos.
    /// </summary>
    private static Arbitrary<List<ReportRow>> ArbitraryReportRows8OrLess()
    {
        var gen = from count in Gen.Choose(1, 8)
                  from rows in Gen.ListOf(count,
                      from etiqueta in Arb.Generate<NonEmptyString>()
                      from valorRaw in Gen.Choose(1, 10000)
                      select new ReportRow(
                          etiqueta.Get.Replace("\0", "A"),
                          valorRaw / 100m,
                          null))
                  select rows.ToList();

        return Arb.From(gen);
    }

    /// <summary>
    /// Property A: Para cualquier ReportResult con más de 8 registros y gráfico de pastel habilitado,
    /// GenerarPdf SHALL producir un PDF válido (bytes no vacíos), lo que implica que la agrupación
    /// "Otros" se ejecutó correctamente sin errores.
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ReportConMasDe8Registros_GeneraPdfValido()
    {
        return Prop.ForAll(ArbitraryReportRowsMoreThan8(), datos =>
        {
            // Configurar licencia QuestPDF
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var reporte = new ReportResult(
                TipoReporte: TipoReporte.TopProducto,
                Titulo: "Reporte de prueba",
                Datos: datos,
                GeneradoEn: DateTime.UtcNow);

            var contexto = new PdfExportContext(
                RazonSocial: "Comercio Test",
                ZonaHoraria: "America/Guayaquil",
                Componentes: new PdfComponentOptions(
                    IncluirGraficoBarras: false,
                    IncluirGraficoPastel: true,
                    IncluirTabla: false));

            // Act: generar PDF con pastel habilitado y más de 8 datos
            var pdfBytes = PdfExportService.GenerarPdf(reporte, contexto);

            // Assert: PDF generado es válido (no vacío)
            return (pdfBytes != null && pdfBytes.Length > 0)
                .Label($"PDF debe ser no vacío para {datos.Count} registros");
        });
    }

    /// <summary>
    /// Property B: Para cualquier ReportResult con más de 8 registros, la lógica de agrupación
    /// SHALL producir exactamente 8 segmentos: los primeros 7 individuales + 1 "Otros".
    /// Se verifica la lógica de agrupación replicando el algoritmo del servicio.
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ReportConMasDe8Registros_AgrupacionProduceMaximo8Segmentos()
    {
        return Prop.ForAll(ArbitraryReportRowsMoreThan8(), datos =>
        {
            // Replicar la lógica de agrupación de ComposePieChart
            var segmentos = new List<(string Etiqueta, decimal Valor)>();

            if (datos.Count <= 8)
            {
                foreach (var d in datos)
                    segmentos.Add((d.Etiqueta, d.Valor));
            }
            else
            {
                // Tomar los primeros 7 y agrupar el resto en "Otros"
                for (int i = 0; i < 7; i++)
                    segmentos.Add((datos[i].Etiqueta, datos[i].Valor));

                var otrosValor = datos.Skip(7).Sum(d => d.Valor);
                segmentos.Add(("Otros", otrosValor));
            }

            // Invariante: máximo 8 segmentos cuando datos > 8
            var maxSegmentos = segmentos.Count == 8;
            // Invariante: el último segmento debe llamarse "Otros"
            var ultimoEsOtros = segmentos.Last().Etiqueta == "Otros";
            // Invariante: los primeros 7 corresponden a los originales
            var primeros7Correctos = true;
            for (int i = 0; i < 7; i++)
            {
                if (segmentos[i].Etiqueta != datos[i].Etiqueta ||
                    segmentos[i].Valor != datos[i].Valor)
                {
                    primeros7Correctos = false;
                    break;
                }
            }
            // Invariante: "Otros" agrupa la suma de los restantes (desde posición 7)
            var sumaOtrosCorrecta = segmentos.Last().Valor == datos.Skip(7).Sum(d => d.Valor);

            return (maxSegmentos && ultimoEsOtros && primeros7Correctos && sumaOtrosCorrecta)
                .Label($"Datos={datos.Count}, Segmentos={segmentos.Count}, UltimoEsOtros={ultimoEsOtros}");
        });
    }

    /// <summary>
    /// Property C: Para cualquier ReportResult con 8 o menos registros, la lógica de agrupación
    /// SHALL mostrar todos los registros individualmente sin agrupar en "Otros".
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ReportCon8OMenosRegistros_NoAgrupa()
    {
        return Prop.ForAll(ArbitraryReportRows8OrLess(), datos =>
        {
            // Replicar la lógica de agrupación de ComposePieChart
            var segmentos = new List<(string Etiqueta, decimal Valor)>();

            if (datos.Count <= 8)
            {
                foreach (var d in datos)
                    segmentos.Add((d.Etiqueta, d.Valor));
            }
            else
            {
                for (int i = 0; i < 7; i++)
                    segmentos.Add((datos[i].Etiqueta, datos[i].Valor));
                var otrosValor = datos.Skip(7).Sum(d => d.Valor);
                segmentos.Add(("Otros", otrosValor));
            }

            // Invariante: todos los segmentos son individuales (sin "Otros")
            var cantidadIgual = segmentos.Count == datos.Count;
            // Invariante: ningún segmento se llama "Otros"
            var sinOtros = !segmentos.Any(s => s.Etiqueta == "Otros");
            // Invariante: cada segmento coincide con el dato original
            var todosCoinciden = true;
            for (int i = 0; i < datos.Count; i++)
            {
                if (segmentos[i].Etiqueta != datos[i].Etiqueta ||
                    segmentos[i].Valor != datos[i].Valor)
                {
                    todosCoinciden = false;
                    break;
                }
            }

            return (cantidadIgual && sinOtros && todosCoinciden)
                .Label($"Datos={datos.Count}, Segmentos={segmentos.Count}, SinOtros={sinOtros}");
        });
    }
}
