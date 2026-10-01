using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Metodos de extension para integrar SkiaSharp con QuestPDF.
/// Permite dibujar graficos personalizados (pie chart, etc.) usando SKCanvas.
/// </summary>
public static class SkiaSharpHelpers
{
    /// <summary>
    /// Renderiza graficos vectoriales SVG usando SkiaSharp canvas.
    /// Ideal para graficos de pastel ya que mantiene calidad a cualquier escala.
    /// </summary>
    public static void SkiaSharpSvgCanvas(this IContainer container, Action<SKCanvas, Size> drawOnCanvas)
    {
        container.Svg(size =>
        {
            using var stream = new MemoryStream();

            using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, size.Width, size.Height), stream))
                drawOnCanvas(canvas, size);

            var svgData = stream.ToArray();
            return Encoding.UTF8.GetString(svgData);
        });
    }
}
