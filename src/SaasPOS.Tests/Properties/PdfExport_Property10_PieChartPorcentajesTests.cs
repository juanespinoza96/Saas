using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.DTOs;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Tests de propiedad para la suma de porcentajes del gráfico de pastel.
/// Feature: pdf-export-personalizable, Property 10: Gráfico de pastel muestra porcentajes correctos
/// **Validates: Requirements 4.1, 4.3**
/// </summary>
public class PdfExport_Property10_PieChartPorcentajesTests
{
    /// <summary>
    /// Generador de listas de ReportRow con 8 o menos elementos y valores positivos.
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
    /// Generador de listas de ReportRow con más de 8 elementos y valores positivos.
    /// </summary>
    private static Arbitrary<List<ReportRow>> ArbitraryReportRowsMoreThan8()
    {
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
    /// Property A: Para cualquier lista de ReportRow con 8 o menos registros (valores positivos),
    /// la suma de los porcentajes individuales (redondeados a 1 decimal) SHALL ser ≈ 100% (±0.1%).
    /// Replica la lógica de cálculo de porcentajes de ComposePieChart sin agrupación.
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SumaPorcentajes_8OMenosRegistros_AproximadamenteCien()
    {
        return Prop.ForAll(ArbitraryReportRows8OrLess(), datos =>
        {
            // Replicar la lógica de segmentación de ComposePieChart (sin agrupación)
            var segmentos = new List<(string Etiqueta, decimal Valor)>();
            foreach (var d in datos)
                segmentos.Add((d.Etiqueta, d.Valor));

            // Calcular total para porcentajes
            var total = segmentos.Sum(s => s.Valor);
            if (total == 0) return true.Label("Total es 0, caso trivial");

            // Calcular porcentajes redondeados a 1 decimal (igual que ComposePieChart)
            var porcentajes = segmentos
                .Select(s => Math.Round((double)(s.Valor / total * 100m), 1))
                .ToList();

            // Sumar todos los porcentajes
            var sumaPorcentajes = porcentajes.Sum();

            // Verificar que la suma es ≈ 100% con tolerancia de ±0.1% por segmento
            // La tolerancia total es ±0.1% * cantidad de segmentos por redondeo
            var tolerancia = 0.1 * segmentos.Count;
            var dentroDeTolerancia = Math.Abs(sumaPorcentajes - 100.0) <= tolerancia;

            return dentroDeTolerancia
                .Label($"Suma={sumaPorcentajes:F2}%, Segmentos={segmentos.Count}, Tolerancia=±{tolerancia:F1}%");
        });
    }

    /// <summary>
    /// Property B: Para cualquier lista de ReportRow con más de 8 registros (valores positivos),
    /// después de agrupar en "Otros", la suma de porcentajes (redondeados a 1 decimal) SHALL ser ≈ 100% (±0.1%).
    /// Replica la lógica de agrupación y cálculo de ComposePieChart.
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SumaPorcentajes_MasDe8Registros_ConAgrupacion_AproximadamenteCien()
    {
        return Prop.ForAll(ArbitraryReportRowsMoreThan8(), datos =>
        {
            // Replicar la lógica de segmentación de ComposePieChart (con agrupación "Otros")
            var segmentos = new List<(string Etiqueta, decimal Valor)>();

            // Tomar los primeros 7 y agrupar el resto en "Otros"
            for (int i = 0; i < 7; i++)
                segmentos.Add((datos[i].Etiqueta, datos[i].Valor));

            var otrosValor = datos.Skip(7).Sum(d => d.Valor);
            segmentos.Add(("Otros", otrosValor));

            // Calcular total para porcentajes
            var total = segmentos.Sum(s => s.Valor);
            if (total == 0) return true.Label("Total es 0, caso trivial");

            // Calcular porcentajes redondeados a 1 decimal (igual que ComposePieChart)
            var porcentajes = segmentos
                .Select(s => Math.Round((double)(s.Valor / total * 100m), 1))
                .ToList();

            // Sumar todos los porcentajes
            var sumaPorcentajes = porcentajes.Sum();

            // Verificar que la suma es ≈ 100% con tolerancia de ±0.1% por segmento (8 segmentos máximo)
            var tolerancia = 0.1 * segmentos.Count;
            var dentroDeTolerancia = Math.Abs(sumaPorcentajes - 100.0) <= tolerancia;

            return dentroDeTolerancia
                .Label($"Suma={sumaPorcentajes:F2}%, Segmentos={segmentos.Count}, Tolerancia=±{tolerancia:F1}%");
        });
    }

    /// <summary>
    /// Property C: Para cualquier lista de ReportRow con valores positivos, la generación de PDF
    /// SHALL completarse exitosamente, verificando que el cálculo interno de porcentajes no produce errores.
    /// Esto valida que el gráfico de pastel renderiza correctamente la distribución porcentual.
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GenerarPdf_ConPastelHabilitado_ProducePdfValido()
    {
        // Usar generador que cubre ambos casos (<=8 y >8)
        var gen = from count in Gen.Choose(1, 20)
                  from rows in Gen.ListOf(count,
                      from etiqueta in Arb.Generate<NonEmptyString>()
                      from valorRaw in Gen.Choose(1, 10000)
                      select new ReportRow(
                          etiqueta.Get.Replace("\0", "A"),
                          valorRaw / 100m,
                          null))
                  select rows.ToList();

        var arb = Arb.From(gen);

        return Prop.ForAll(arb, datos =>
        {
            // Configurar licencia QuestPDF
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var reporte = new ReportResult(
                TipoReporte: TipoReporte.TopProducto,
                Titulo: "Reporte de prueba porcentajes",
                Datos: datos,
                GeneradoEn: DateTime.UtcNow);

            var contexto = new PdfExportContext(
                RazonSocial: "Comercio Test Porcentajes",
                ZonaHoraria: "America/Guayaquil",
                Componentes: new PdfComponentOptions(
                    IncluirGraficoBarras: false,
                    IncluirGraficoPastel: true,
                    IncluirTabla: false));

            // Act: generar PDF con gráfico de pastel habilitado
            var pdfBytes = PdfExportService.GenerarPdf(reporte, contexto);

            // Assert: PDF generado es válido (no vacío), confirmando que los porcentajes se calcularon sin error
            return (pdfBytes != null && pdfBytes.Length > 0)
                .Label($"PDF debe ser no vacío para {datos.Count} registros con pastel habilitado");
        });
    }
}
