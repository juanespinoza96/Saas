using System.Text;
using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.DTOs;
using SaasPOS.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Tests de propiedad para verificar que CSV ignora personalización visual.
/// Feature: pdf-export-personalizable, Property 8: CSV ignora personalización visual
/// **Validates: Requirements 5.1, 5.2**
/// </summary>
public class PdfExport_Property8_CsvIgnoraPersonalizacionTests
{
    /// <summary>
    /// Property A: Para cualquier PdfComponentOptions arbitrario y cualquier ReportResult,
    /// la exportación CSV siempre produce exactamente el header "Etiqueta,Valor,Detalle"
    /// y las filas correspondientes, sin importar las opciones de personalización visual.
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CsvExport_IgnoraComponentOptions_SiempreProduceEstructuraFija()
    {
        return Prop.ForAll(
            ArbitraryPdfComponentOptions(),
            ArbitraryReportResult(),
            (opciones, reporte) =>
            {
                // Actuar: exportar CSV (no recibe PdfComponentOptions por diseño)
                var engine = CrearReportEngine();
                var csvBytes = engine.ExportarCsvAsync(reporte).Result;
                var csvText = Encoding.UTF8.GetString(csvBytes);
                var lineas = csvText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                // Verificar header exacto
                var headerCorrecto = lineas.Length > 0 && lineas[0] == "Etiqueta,Valor,Detalle";

                // Verificar cantidad de filas de datos = cantidad de registros en Datos
                var cantidadFilasDatos = lineas.Length - 1; // descontar header
                var cantidadCorrecta = cantidadFilasDatos == reporte.Datos.Count;

                return (headerCorrecto && cantidadCorrecta)
                    .Label($"Header={lineas.FirstOrDefault()}, FilasDatos={cantidadFilasDatos}, Esperadas={reporte.Datos.Count}, Opciones=[Barras={opciones.IncluirGraficoBarras}, Pastel={opciones.IncluirGraficoPastel}, Tabla={opciones.IncluirTabla}]");
            });
    }

    /// <summary>
    /// Property B: Para dos PdfComponentOptions distintos y el mismo ReportResult,
    /// la salida CSV es idéntica — demostrando que las opciones visuales NO afectan el CSV.
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CsvExport_MismoResultado_IndependienteDePdfComponentOptions()
    {
        return Prop.ForAll(
            ArbitraryPdfComponentOptions(),
            ArbitraryPdfComponentOptions(),
            ArbitraryReportResult(),
            (opciones1, opciones2, reporte) =>
            {
                var engine = CrearReportEngine();

                // Exportar CSV dos veces con el mismo reporte (opciones no participan)
                var csvBytes1 = engine.ExportarCsvAsync(reporte).Result;
                var csvBytes2 = engine.ExportarCsvAsync(reporte).Result;

                var csv1 = Encoding.UTF8.GetString(csvBytes1);
                var csv2 = Encoding.UTF8.GetString(csvBytes2);

                // Independientemente de las opciones generadas, la salida es idéntica
                var resultadoIdentico = csv1 == csv2;

                return resultadoIdentico
                    .Label($"CSV idéntico para opciones [{opciones1.IncluirGraficoBarras},{opciones1.IncluirGraficoPastel},{opciones1.IncluirTabla}] vs [{opciones2.IncluirGraficoBarras},{opciones2.IncluirGraficoPastel},{opciones2.IncluirTabla}]");
            });
    }

    /// <summary>
    /// Property C: Cada fila del CSV tiene exactamente 3 columnas separadas por coma,
    /// correspondientes a Etiqueta, Valor, Detalle.
    /// **Validates: Requirements 5.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CsvExport_CadaFilaTieneExactamenteTresColumnas()
    {
        return Prop.ForAll(
            ArbitraryReportResultSinComasEnEtiqueta(),
            reporte =>
            {
                var engine = CrearReportEngine();
                var csvBytes = engine.ExportarCsvAsync(reporte).Result;
                var csvText = Encoding.UTF8.GetString(csvBytes);
                var lineas = csvText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                // El header siempre tiene 3 columnas
                var headerTresColumnas = lineas[0].Split(',').Length == 3;

                // Cada fila de datos tiene exactamente 3 columnas (sin comas en campos)
                var todasFilasTresColumnas = lineas.Skip(1).All(linea =>
                {
                    var columnas = linea.Split(',');
                    return columnas.Length == 3;
                });

                return (headerTresColumnas && todasFilasTresColumnas)
                    .Label($"Header3cols={headerTresColumnas}, TodasFilas3cols={todasFilasTresColumnas}, TotalLineas={lineas.Length}");
            });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Crea una instancia de ReportEngine con dependencias mock (solo se usa ExportarCsvAsync).
    /// </summary>
    private static ReportEngine CrearReportEngine()
    {
        var mockLogger = new Mock<ILogger<ReportEngine>>();
        // ExportarCsvAsync no usa DbContext, así que pasamos null con seguridad
        return new ReportEngine(null!, mockLogger.Object);
    }

    /// <summary>
    /// Generador de PdfComponentOptions arbitrario con todas las combinaciones de bools.
    /// </summary>
    private static Arbitrary<PdfComponentOptions> ArbitraryPdfComponentOptions()
    {
        var gen = from incluirBarras in Arb.Generate<bool>()
                  from incluirPastel in Arb.Generate<bool>()
                  from incluirTabla in Arb.Generate<bool>()
                  select new PdfComponentOptions(incluirBarras, incluirPastel, incluirTabla);

        return Arb.From(gen);
    }

    /// <summary>
    /// Generador de ReportResult arbitrario con datos aleatorios.
    /// </summary>
    private static Arbitrary<ReportResult> ArbitraryReportResult()
    {
        var etiquetaGen = Gen.Elements(
            "Producto A", "Producto B", "Categoría X", "Sucursal Norte",
            "Arroz", "Leche", "Pan", "Café", "Azúcar", "Aceite");

        var detalleGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("unidades vendidas", "monto total", "kilogramos", "litros"));

        var rowGen = from etiqueta in etiquetaGen
                     from valor in Gen.Choose(1, 100000).Select(v => (decimal)v / 100m)
                     from detalle in detalleGen
                     select new ReportRow(etiqueta, valor, detalle);

        var tipoGen = Gen.Elements(
            TipoReporte.TopProducto, TipoReporte.TopCategoria,
            TipoReporte.TopSucursal, TipoReporte.Personalizado);

        var tituloGen = Gen.Elements(
            "Top Productos", "Top Categorías", "Top Sucursales", "Reporte Personalizado");

        var gen = from tipo in tipoGen
                  from titulo in tituloGen
                  from cantidadRows in Gen.Choose(0, 20)
                  from rows in Gen.ListOf(cantidadRows, rowGen)
                  from anio in Gen.Choose(2020, 2030)
                  from mes in Gen.Choose(1, 12)
                  from dia in Gen.Choose(1, 28)
                  select new ReportResult(
                      tipo,
                      titulo,
                      rows.ToList(),
                      new DateTime(anio, mes, dia, 12, 0, 0, DateTimeKind.Utc));

        return Arb.From(gen);
    }

    /// <summary>
    /// Generador de ReportResult con etiquetas y detalles que NO contienen comas ni comillas,
    /// para verificar la estructura de 3 columnas de forma simple.
    /// </summary>
    private static Arbitrary<ReportResult> ArbitraryReportResultSinComasEnEtiqueta()
    {
        // Etiquetas sin caracteres especiales CSV
        var etiquetaGen = Gen.Elements(
            "Producto A", "Producto B", "Categoría X", "Sucursal Norte",
            "Arroz", "Leche", "Pan", "Café", "Azúcar", "Aceite");

        // Detalles sin comas ni comillas
        var detalleGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("unidades vendidas", "monto total", "kilogramos", "litros"));

        var rowGen = from etiqueta in etiquetaGen
                     from valor in Gen.Choose(1, 100000).Select(v => (decimal)v / 100m)
                     from detalle in detalleGen
                     select new ReportRow(etiqueta, valor, detalle);

        var gen = from cantidadRows in Gen.Choose(1, 15)
                  from rows in Gen.ListOf(cantidadRows, rowGen)
                  select new ReportResult(
                      TipoReporte.TopProducto,
                      "Reporte Test",
                      rows.ToList(),
                      DateTime.UtcNow);

        return Arb.From(gen);
    }
}
