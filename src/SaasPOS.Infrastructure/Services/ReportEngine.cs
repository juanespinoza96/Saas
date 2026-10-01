using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Report generation engine. Calculations based exclusively on Ventas/DetalleVentas
/// of the authenticated ComercioId (Req 13.4).
/// </summary>
public class ReportEngine : IReportEngine
{
    private const string PlanBasico = "BÃ¡sico";
    private const string PlanIntermedio = "Intermedio";
    private const string PlanEmpresarial = "Empresarial";

    private readonly AppDbContext _db;
    private readonly ILogger<ReportEngine> _logger;

    public ReportEngine(AppDbContext db, ILogger<ReportEngine> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ReportResult> GenerarReporteAsync(ReportRequest request, int comercioId, string planNivel)
    {
        // Plan BÃ¡sico: blocked (Req 13.1)
        if (planNivel == PlanBasico)
            throw new ReportForbiddenException("El Plan BÃ¡sico no incluye acceso a reportes. Actualice su plan para usar esta funcionalidad.");

        // Plan Intermedio: only predefined reports, no custom filters (Req 13.2)
        if (planNivel == PlanIntermedio)
        {
            if (request.TipoReporte == TipoReporte.Personalizado)
                throw new ReportForbiddenException("El Plan Intermedio no permite reportes personalizados. Actualice al Plan Empresarial.");

            // No custom filters allowed â€” force current month only
            var now = DateTime.UtcNow;
            var fechaDesde = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var fechaHasta = fechaDesde.AddMonths(1).AddTicks(-1);

            return await GenerarReportePredefinidoAsync(request.TipoReporte, comercioId, fechaDesde, fechaHasta);
        }

        // Plan Empresarial: all reports + custom filters (Req 13.3)
        if (planNivel == PlanEmpresarial)
        {
            if (request.TipoReporte == TipoReporte.Personalizado)
                return await GenerarReportePersonalizadoAsync(comercioId, request);

            // Predefined reports with optional date filters
            var fechaDesde = request.FechaDesde ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var fechaHasta = request.FechaHasta ?? DateTime.UtcNow;

            return await GenerarReportePredefinidoAsync(request.TipoReporte, comercioId, fechaDesde, fechaHasta);
        }

        throw new ReportForbiddenException("No se pudo determinar el nivel de plan. Acceso denegado.");
    }

    public Task<byte[]> ExportarPdfAsync(ReportResult reporte, PdfExportContext contexto)
    {
        // Delega a PdfExportService con contexto completo (branding, zona horaria, componentes)
        var pdfBytes = PdfExportService.GenerarPdf(reporte, contexto);
        return Task.FromResult(pdfBytes);
    }

    public Task<byte[]> ExportarPdfAsync(ReportResult reporte)
    {
        // Generar PDF real con QuestPDF (tabla + grafico de barras)
        var pdfBytes = PdfExportService.GenerarPdf(reporte);
        return Task.FromResult(pdfBytes);
    }

    public Task<byte[]> ExportarCsvAsync(ReportResult reporte)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Etiqueta,Valor,Detalle");

        foreach (var row in reporte.Datos)
        {
            var etiqueta = EscapeCsv(row.Etiqueta);
            var valor = row.Valor.ToString("F2", CultureInfo.InvariantCulture);
            var detalle = EscapeCsv(row.Detalle ?? "");
            sb.AppendLine($"{etiqueta},{valor},{detalle}");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Task.FromResult(bytes);
    }

    // â”€â”€ Private report generators â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task<ReportResult> GenerarReportePredefinidoAsync(
        TipoReporte tipo, int comercioId, DateTime fechaDesde, DateTime fechaHasta)
    {
        return tipo switch
        {
            TipoReporte.TopProducto => await GenerarTopProductoAsync(comercioId, fechaDesde, fechaHasta),
            TipoReporte.TopCategoria => await GenerarTopCategoriaAsync(comercioId, fechaDesde, fechaHasta),
            TipoReporte.TopSucursal => await GenerarTopSucursalAsync(comercioId, fechaDesde, fechaHasta),
            _ => throw new ReportForbiddenException($"Tipo de reporte '{tipo}' no es un reporte predefinido vÃ¡lido.")
        };
    }

    private async Task<ReportResult> GenerarTopProductoAsync(int comercioId, DateTime fechaDesde, DateTime fechaHasta)
    {
        // Top products by quantity sold â€” based on DetalleVentas joined with Ventas for ComercioId filter
        var datos = await _db.DetalleVentas
            .Where(d => d.Venta.ComercioId == comercioId
                     && d.Venta.FechaVenta >= fechaDesde
                     && d.Venta.FechaVenta <= fechaHasta)
            .GroupBy(d => new { d.ProductoId, d.Producto.Nombre })
            .Select(g => new
            {
                ProductoNombre = g.Key.Nombre,
                CantidadTotal = g.Sum(x => x.Cantidad)
            })
            .OrderByDescending(x => x.CantidadTotal)
            .Take(10)
            .ToListAsync();

        var rows = datos.Select(d => new ReportRow(
            Etiqueta: d.ProductoNombre,
            Valor: d.CantidadTotal,
            Detalle: "unidades vendidas"
        )).ToList();

        return new ReportResult(
            TipoReporte: TipoReporte.TopProducto,
            Titulo: $"Top Productos por Cantidad Vendida ({fechaDesde:dd/MM/yyyy} - {fechaHasta:dd/MM/yyyy})",
            Datos: rows,
            GeneradoEn: DateTime.UtcNow);
    }

    private async Task<ReportResult> GenerarTopCategoriaAsync(int comercioId, DateTime fechaDesde, DateTime fechaHasta)
    {
        // Top categories by total sales amount
        var datos = await _db.DetalleVentas
            .Where(d => d.Venta.ComercioId == comercioId
                     && d.Venta.FechaVenta >= fechaDesde
                     && d.Venta.FechaVenta <= fechaHasta
                     && d.Producto.CategoriaId != null)
            .GroupBy(d => new { d.Producto.CategoriaId, d.Producto.Categoria!.Nombre })
            .Select(g => new
            {
                CategoriaNombre = g.Key.Nombre,
                MontoTotal = g.Sum(x => x.Cantidad * x.PrecioRealCobrado)
            })
            .OrderByDescending(x => x.MontoTotal)
            .Take(10)
            .ToListAsync();

        var rows = datos.Select(d => new ReportRow(
            Etiqueta: d.CategoriaNombre,
            Valor: d.MontoTotal,
            Detalle: "monto total vendido"
        )).ToList();

        return new ReportResult(
            TipoReporte: TipoReporte.TopCategoria,
            Titulo: $"Top CategorÃ­as por Monto de Ventas ({fechaDesde:dd/MM/yyyy} - {fechaHasta:dd/MM/yyyy})",
            Datos: rows,
            GeneradoEn: DateTime.UtcNow);
    }

    private async Task<ReportResult> GenerarTopSucursalAsync(int comercioId, DateTime fechaDesde, DateTime fechaHasta)
    {
        // Top branches by total sales amount
        var datos = await _db.Ventas
            .Where(v => v.ComercioId == comercioId
                     && v.FechaVenta >= fechaDesde
                     && v.FechaVenta <= fechaHasta)
            .GroupBy(v => new { v.SucursalId, v.Sucursal.Nombre })
            .Select(g => new
            {
                SucursalNombre = g.Key.Nombre,
                MontoTotal = g.Sum(v => v.Total)
            })
            .OrderByDescending(x => x.MontoTotal)
            .Take(10)
            .ToListAsync();

        var rows = datos.Select(d => new ReportRow(
            Etiqueta: d.SucursalNombre,
            Valor: d.MontoTotal,
            Detalle: "monto total vendido"
        )).ToList();

        return new ReportResult(
            TipoReporte: TipoReporte.TopSucursal,
            Titulo: $"Top Sucursales por Monto de Ventas ({fechaDesde:dd/MM/yyyy} - {fechaHasta:dd/MM/yyyy})",
            Datos: rows,
            GeneradoEn: DateTime.UtcNow);
    }

    private async Task<ReportResult> GenerarReportePersonalizadoAsync(int comercioId, ReportRequest request)
    {
        // Custom report with filters â€” Plan Empresarial only
        var fechaDesde = request.FechaDesde ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var fechaHasta = request.FechaHasta ?? DateTime.UtcNow;

        var query = _db.DetalleVentas
            .Where(d => d.Venta.ComercioId == comercioId
                     && d.Venta.FechaVenta >= fechaDesde
                     && d.Venta.FechaVenta <= fechaHasta);

        // Apply optional filters
        if (request.SucursalId.HasValue)
            query = query.Where(d => d.Venta.SucursalId == request.SucursalId.Value);

        if (request.CategoriaId.HasValue)
            query = query.Where(d => d.Producto.CategoriaId == request.CategoriaId.Value);

        if (request.ProductoId.HasValue)
            query = query.Where(d => d.ProductoId == request.ProductoId.Value);

        if (!string.IsNullOrWhiteSpace(request.MetodoPago))
            query = query.Where(d => d.Venta.MetodoPago == request.MetodoPago);

        var datos = await query
            .GroupBy(d => new { d.ProductoId, d.Producto.Nombre })
            .Select(g => new
            {
                ProductoNombre = g.Key.Nombre,
                CantidadTotal = g.Sum(x => x.Cantidad),
                MontoTotal = g.Sum(x => x.Cantidad * x.PrecioRealCobrado)
            })
            .OrderByDescending(x => x.MontoTotal)
            .Take(50)
            .ToListAsync();

        var rows = datos.Select(d => new ReportRow(
            Etiqueta: d.ProductoNombre,
            Valor: d.MontoTotal,
            Detalle: $"{d.CantidadTotal:F2} unidades"
        )).ToList();

        var filtrosAplicados = BuildFiltrosDescripcion(request);

        return new ReportResult(
            TipoReporte: TipoReporte.Personalizado,
            Titulo: $"Reporte Personalizado ({fechaDesde:dd/MM/yyyy} - {fechaHasta:dd/MM/yyyy}){filtrosAplicados}",
            Datos: rows,
            GeneradoEn: DateTime.UtcNow);
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string BuildFiltrosDescripcion(ReportRequest request)
    {
        var filtros = new List<string>();
        if (request.SucursalId.HasValue) filtros.Add($"Sucursal={request.SucursalId}");
        if (request.CategoriaId.HasValue) filtros.Add($"CategorÃ­a={request.CategoriaId}");
        if (request.ProductoId.HasValue) filtros.Add($"Producto={request.ProductoId}");
        if (!string.IsNullOrWhiteSpace(request.MetodoPago)) filtros.Add($"MetodoPago={request.MetodoPago}");

        return filtros.Count > 0 ? $" | Filtros: {string.Join(", ", filtros)}" : "";
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

/// <summary>
/// Custom exception for plan-based report access violations.
/// </summary>
public class ReportForbiddenException : Exception
{
    public ReportForbiddenException(string message) : base(message) { }
}
