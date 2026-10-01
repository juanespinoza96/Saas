namespace SaasPOS.Application.DTOs;

// ── Enums ────────────────────────────────────────────────────────────────────

public enum TipoReporte
{
    TopProducto,
    TopCategoria,
    TopSucursal,
    Personalizado
}

public enum FormatoExportacion
{
    Pdf,
    Csv
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record ReportRequest(
    TipoReporte TipoReporte,
    DateTime? FechaDesde = null,
    DateTime? FechaHasta = null,
    int? SucursalId = null,
    int? CategoriaId = null,
    int? ProductoId = null,
    string? MetodoPago = null);

// ── Result DTOs ──────────────────────────────────────────────────────────────

public record ReportResult(
    TipoReporte TipoReporte,
    string Titulo,
    List<ReportRow> Datos,
    DateTime GeneradoEn);

/// <summary>
/// Generic report row with labeled columns for flexible rendering.
/// </summary>
public record ReportRow(
    string Etiqueta,
    decimal Valor,
    string? Detalle = null);

// ── API Response DTOs ────────────────────────────────────────────────────────

public record ReporteResponse(
    string TipoReporte,
    string Titulo,
    List<ReportRow> Datos,
    DateTime GeneradoEn);

public record ExportarReporteRequest(
    TipoReporte TipoReporte,
    FormatoExportacion Formato,
    DateTime? FechaDesde = null,
    DateTime? FechaHasta = null,
    int? SucursalId = null,
    int? CategoriaId = null,
    int? ProductoId = null,
    string? MetodoPago = null);

// ── PDF Export Personalizable ────────────────────────────────────────────────

/// <summary>
/// Opciones de personalización de componentes PDF.
/// Solo aplicable al Plan Empresarial.
/// </summary>
public record PdfComponentOptions(
    bool IncluirGraficoBarras = true,
    bool IncluirGraficoPastel = true,
    bool IncluirTabla = true);

/// <summary>
/// Contexto completo para generación de PDF personalizado.
/// Agrupa datos del comercio y opciones de exportación.
/// </summary>
public record PdfExportContext(
    string RazonSocial,
    string ZonaHoraria,
    PdfComponentOptions Componentes);
