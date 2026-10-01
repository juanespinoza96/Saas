using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using SaasPOS.Application.DTOs;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Servicio de exportacion a PDF real usando QuestPDF.
/// ---
/// Genera documentos profesionales con tabla de datos y grafico de barras.
/// </summary>
public static class PdfExportService
{
    /// <summary>
    /// Zona horaria por defecto cuando el comercio no tiene configurada o es invalida.
    /// </summary>
    private const string ZonaHorariaDefault = "America/Guayaquil";

    /// <summary>
    /// Genera un documento PDF con tabla de datos y grafico de barras horizontal.
    /// [Backward compatible] Mantiene comportamiento original sin personalización.
    /// </summary>
    public static byte[] GenerarPdf(ReportResult reporte)
    {
        // Configurar licencia comunitaria de QuestPDF
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                // Encabezado
                page.Header().Element(c => ComposeHeader(c, reporte));

                // Contenido: tabla + grafico
                page.Content().Element(c => ComposeContent(c, reporte));

                // Pie de pagina
                page.Footer().Element(ComposeFooter);
            });
        });

        using var stream = new MemoryStream();
        document.GeneratePdf(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Genera un documento PDF personalizado con branding del comercio,
    /// hora local convertida y componentes visuales configurables.
    /// </summary>
    public static byte[] GenerarPdf(ReportResult reporte, PdfExportContext contexto)
    {
        // Configurar licencia comunitaria de QuestPDF
        QuestPDF.Settings.License = LicenseType.Community;

        // Convertir fecha de generacion a zona horaria local del comercio
        var (fechaLocal, offsetTexto) = ConvertirAZonaHorariaLocal(reporte.GeneradoEn, contexto.ZonaHoraria);

        // Resolver RazonSocial con fallback
        var razonSocial = string.IsNullOrWhiteSpace(contexto.RazonSocial)
            ? "Comercio Sin Nombre"
            : contexto.RazonSocial;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                // Encabezado personalizado con branding del comercio
                page.Header().Element(c => ComposeHeaderPersonalizado(c, reporte, razonSocial, fechaLocal, offsetTexto));

                // Contenido: componentes segun opciones
                page.Content().Element(c => ComposeContentPersonalizado(c, reporte, contexto.Componentes));

                // Pie de pagina
                page.Footer().Element(ComposeFooter);
            });
        });

        using var stream = new MemoryStream();
        document.GeneratePdf(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Convierte una fecha UTC a la zona horaria IANA especificada.
    /// Si la zona es invalida, hace fallback a "America/Guayaquil".
    /// Retorna la fecha local y el offset formateado (ej: "-05:00").
    /// </summary>
    internal static (DateTime fechaLocal, string offsetTexto) ConvertirAZonaHorariaLocal(DateTime fechaUtc, string? zonaHoraria)
    {
        var zonaId = string.IsNullOrWhiteSpace(zonaHoraria) ? ZonaHorariaDefault : zonaHoraria;

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(zonaId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback a zona horaria de Ecuador si la ID IANA es invalida
            tz = TimeZoneInfo.FindSystemTimeZoneById(ZonaHorariaDefault);
        }
        catch (InvalidTimeZoneException)
        {
            // Fallback a zona horaria de Ecuador si la zona es corrupta
            tz = TimeZoneInfo.FindSystemTimeZoneById(ZonaHorariaDefault);
        }

        var fechaLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(fechaUtc, DateTimeKind.Utc), tz);

        var offset = tz.GetUtcOffset(fechaLocal);
        var signo = offset < TimeSpan.Zero ? "-" : "+";
        var offsetTexto = $"{signo}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}";

        return (fechaLocal, offsetTexto);
    }

    /// <summary>
    /// Encabezado personalizado con RazonSocial del comercio y fecha en hora local.
    /// </summary>
    private static void ComposeHeaderPersonalizado(
        IContainer container,
        ReportResult reporte,
        string razonSocial,
        DateTime fechaLocal,
        string offsetTexto)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    // Titulo principal: RazonSocial del comercio
                    c.Item().Text(razonSocial).Bold().FontSize(18).FontColor(Colors.Blue.Darken2);
                    // Subtitulo secundario
                    c.Item().Text("Powered by SaasPOS").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(180).AlignRight().Column(c =>
                {
                    // Fecha en hora local con offset de zona horaria
                    var fechaFormateada = fechaLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                    c.Item().Text($"Generado: {fechaFormateada} ({offsetTexto})")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text($"Tipo: {reporte.TipoReporte}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });

            col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Blue.Darken2);

            col.Item().PaddingBottom(5).Text(reporte.Titulo)
                .Bold().FontSize(13).FontColor(Colors.Grey.Darken3);
        });
    }

    /// <summary>
    /// Contenido personalizado con componentes condicionales segun PdfComponentOptions.
    /// </summary>
    private static void ComposeContentPersonalizado(IContainer container, ReportResult reporte, PdfComponentOptions componentes)
    {
        container.PaddingTop(10).Column(col =>
        {
            // Grafico de barras (condicional)
            if (componentes.IncluirGraficoBarras && reporte.Datos.Count > 0)
            {
                col.Item().PaddingBottom(10).Text("Resumen Grafico").Bold().FontSize(11);
                col.Item().Height(ComputeChartHeight(reporte.Datos.Count)).Element(c => ComposeBarChart(c, reporte));
                col.Item().PaddingVertical(15).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            }

            // Grafico de pastel (condicional)
            if (componentes.IncluirGraficoPastel && reporte.Datos.Count > 0)
            {
                col.Item().PaddingBottom(10).Text("Distribucion Porcentual").Bold().FontSize(11);
                col.Item().Height(220).Element(c => ComposePieChart(c, reporte));
                col.Item().PaddingVertical(15).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            }
            else if (componentes.IncluirGraficoPastel && reporte.Datos.Count == 0)
            {
                col.Item().PaddingBottom(10).Text("Distribucion Porcentual").Bold().FontSize(11);
                col.Item().Text("Sin datos para graficar").FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingVertical(15).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            }

            // Tabla de datos (condicional)
            if (componentes.IncluirTabla)
            {
                col.Item().PaddingBottom(5).Text("Detalle de Datos").Bold().FontSize(11);
                col.Item().Element(c => ComposeTable(c, reporte));
            }

            // Resumen final (siempre visible)
            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
                {
                    c.Item().Text($"Total de registros: {reporte.Datos.Count}").FontSize(9);
                    if (reporte.Datos.Count > 0)
                    {
                        var total = reporte.Datos.Sum(d => d.Valor);
                        c.Item().Text($"Valor total: {total.ToString("N2", CultureInfo.InvariantCulture)}").FontSize(9).Bold();
                    }
                });
            });
        });
    }

    /// <summary>
    /// Renderiza un grafico de pastel (pie chart) usando Canvas de QuestPDF con SkiaSharp.
    /// Agrupa registros a partir de la posicion 8 en categoria "Otros".
    /// Muestra etiqueta y porcentaje con 1 decimal junto a cada segmento.
    /// </summary>
    private static void ComposePieChart(IContainer container, ReportResult reporte)
    {
        // Paleta de colores con contraste adecuado entre segmentos adyacentes
        var pieColors = new[] { "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6", "#EC4899", "#0EA5E9", "#22C55E", "#F97316" };

        // Preparar datos: agrupar en "Otros" si hay mas de 8 registros
        var datos = reporte.Datos;
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

        // Calcular total para porcentajes
        var total = segmentos.Sum(s => s.Valor);
        if (total == 0) return;

        container.SkiaSharpSvgCanvas((skCanvas, size) =>
        {

            // Dimensiones del grafico
            var chartDiameter = Math.Min(size.Width * 0.4f, size.Height - 20);
            var centerX = chartDiameter / 2 + 10;
            var centerY = size.Height / 2;
            var radius = chartDiameter / 2 - 5;

            // Area del circulo para dibujar arcos
            var rect = new SKRect(
                centerX - radius,
                centerY - radius,
                centerX + radius,
                centerY + radius);

            float startAngle = -90; // Comenzar desde arriba (12 en punto)

            for (int i = 0; i < segmentos.Count; i++)
            {
                var (etiqueta, valor) = segmentos[i];
                var porcentaje = (float)(valor / total * 100);
                var sweepAngle = porcentaje / 100f * 360f;

                // Dibujar segmento del pastel
                var colorHex = pieColors[i % pieColors.Length];
                using var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = SKColor.Parse(colorHex),
                    IsAntialias = true
                };

                using var path = new SKPath();
                path.MoveTo(centerX, centerY);
                path.ArcTo(rect, startAngle, sweepAngle, false);
                path.Close();
                skCanvas.DrawPath(path, paint);

                // Dibujar borde del segmento para separacion visual
                using var borderPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.White,
                    StrokeWidth = 1.5f,
                    IsAntialias = true
                };
                skCanvas.DrawPath(path, borderPaint);

                startAngle += sweepAngle;
            }

            // Dibujar leyenda a la derecha del grafico
            var legendX = centerX + radius + 30;
            var legendY = 20f;
            var legendItemHeight = 18f;

            using var legendFont = new SKFont(SKTypeface.Default, 9);

            for (int i = 0; i < segmentos.Count; i++)
            {
                var (etiqueta, valor) = segmentos[i];
                var porcentaje = (decimal)(valor / total * 100);
                var porcentajeTexto = porcentaje.ToString("F1", CultureInfo.InvariantCulture);
                var colorHex = pieColors[i % pieColors.Length];

                // Cuadro de color
                using var colorPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = SKColor.Parse(colorHex),
                    IsAntialias = true
                };
                skCanvas.DrawRect(legendX, legendY + (i * legendItemHeight), 10, 10, colorPaint);

                // Texto de la leyenda: etiqueta truncada + porcentaje
                var labelText = etiqueta.Length > 18 ? etiqueta[..15] + "..." : etiqueta;
                var legendText = $"{labelText} ({porcentajeTexto}%)";

                using var textPaint = new SKPaint
                {
                    Color = SKColor.Parse("#374151"),
                    IsAntialias = true
                };
                skCanvas.DrawText(legendText, legendX + 14, legendY + (i * legendItemHeight) + 9, legendFont, textPaint);
            }
        });
    }

    private static void ComposeHeader(IContainer container, ReportResult reporte)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("SaasPOS").Bold().FontSize(18).FontColor(Colors.Blue.Darken2);
                    c.Item().Text("Sistema de Punto de Venta").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(150).AlignRight().Column(c =>
                {
                    c.Item().Text($"Generado: {reporte.GeneradoEn:dd/MM/yyyy HH:mm}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text($"Tipo: {reporte.TipoReporte}").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });

            col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Blue.Darken2);

            col.Item().PaddingBottom(5).Text(reporte.Titulo)
                .Bold().FontSize(13).FontColor(Colors.Grey.Darken3);
        });
    }

    private static void ComposeContent(IContainer container, ReportResult reporte)
    {
        container.PaddingTop(10).Column(col =>
        {
            // Seccion 1: Grafico de barras
            if (reporte.Datos.Count > 0)
            {
                col.Item().PaddingBottom(10).Text("Resumen Grafico").Bold().FontSize(11);
                col.Item().Height(ComputeChartHeight(reporte.Datos.Count)).Element(c => ComposeBarChart(c, reporte));
                col.Item().PaddingVertical(15).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            }

            // Seccion 2: Tabla de datos
            col.Item().PaddingBottom(5).Text("Detalle de Datos").Bold().FontSize(11);
            col.Item().Element(c => ComposeTable(c, reporte));

            // Resumen final
            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
                {
                    c.Item().Text($"Total de registros: {reporte.Datos.Count}").FontSize(9);
                    if (reporte.Datos.Count > 0)
                    {
                        var total = reporte.Datos.Sum(d => d.Valor);
                        c.Item().Text($"Valor total: {total.ToString("N2", CultureInfo.InvariantCulture)}").FontSize(9).Bold();
                    }
                });
            });
        });
    }

    private static void ComposeTable(IContainer container, ReportResult reporte)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(30);   // #
                columns.RelativeColumn(4);    // Etiqueta
                columns.RelativeColumn(2);    // Valor
                columns.RelativeColumn(3);    // Detalle
            });

            // Encabezado de tabla
            table.Header(header =>
            {
                header.Cell().Background(Colors.Blue.Darken2).Padding(5)
                    .Text("#").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Colors.Blue.Darken2).Padding(5)
                    .Text("Etiqueta").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Colors.Blue.Darken2).Padding(5)
                    .Text("Valor").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Colors.Blue.Darken2).Padding(5)
                    .Text("Detalle").FontColor(Colors.White).Bold().FontSize(9);
            });

            // Filas de datos
            for (int i = 0; i < reporte.Datos.Count; i++)
            {
                var row = reporte.Datos[i];
                var bgColor = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                table.Cell().Background(bgColor).Padding(4)
                    .Text($"{i + 1}").FontSize(9);
                table.Cell().Background(bgColor).Padding(4)
                    .Text(row.Etiqueta).FontSize(9);
                table.Cell().Background(bgColor).Padding(4)
                    .Text(row.Valor.ToString("N2", CultureInfo.InvariantCulture)).FontSize(9).Bold();
                table.Cell().Background(bgColor).Padding(4)
                    .Text(row.Detalle ?? "").FontSize(9).FontColor(Colors.Grey.Darken1);
            }
        });
    }

    private static void ComposeBarChart(IContainer container, ReportResult reporte)
    {
        var datos = reporte.Datos.Take(10).ToList();
        if (datos.Count == 0) return;

        var maxVal = datos.Max(d => d.Valor);
        if (maxVal == 0) maxVal = 1;

        // Colores para las barras (hex)
        var barColors = new[] { "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6", "#EC4899", "#0EA5E9", "#22C55E", "#F97316", "#6B7280" };

        container.Column(col =>
        {
            for (int i = 0; i < datos.Count; i++)
            {
                var row = datos[i];
                var proportion = (float)(row.Valor / maxVal);
                var color = barColors[i % barColors.Length];
                var label = row.Etiqueta.Length > 20 ? row.Etiqueta[..17] + "..." : row.Etiqueta;

                col.Item().PaddingBottom(4).Row(r =>
                {
                    // Etiqueta
                    r.ConstantItem(120).AlignRight().PaddingRight(8)
                        .Text(label).FontSize(8);

                    // Barra
                    r.RelativeItem().Height(16).Row(barRow =>
                    {
                        barRow.RelativeItem((int)(proportion * 100)).Background(color)
                            .AlignCenter().AlignMiddle()
                            .Text("").FontSize(1);
                        barRow.RelativeItem((int)((1 - proportion) * 100) + 1)
                            .Text("").FontSize(1);
                    });

                    // Valor numerico
                    r.ConstantItem(70).PaddingLeft(5)
                        .Text(row.Valor.ToString("N2", CultureInfo.InvariantCulture)).FontSize(8).Bold();
                });
            }
        });
    }

    private static float ComputeChartHeight(int itemCount)
    {
        var count = Math.Min(itemCount, 10);
        return count * 28f + 10;
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().AlignLeft()
                .Text("SaasPOS - Reporte generado automaticamente")
                .FontSize(8).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().AlignRight()
                .Text(text =>
                {
                    text.Span("Pagina ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.Span(" de ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                });
        });
    }
}
