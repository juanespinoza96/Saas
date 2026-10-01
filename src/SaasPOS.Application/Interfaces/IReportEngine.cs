using SaasPOS.Application.DTOs;

namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Report generation engine. Validates plan-level access and produces reports
/// based exclusively on Ventas/DetalleVentas of the authenticated ComercioId.
/// Req 13.1–13.5.
/// </summary>
public interface IReportEngine
{
    /// <summary>
    /// Generates a report based on the request type, filtered by ComercioId and plan level.
    /// Plan Básico → throws ForbiddenException; Plan Intermedio → predefined only; Plan Empresarial → all + filters.
    /// </summary>
    Task<ReportResult> GenerarReporteAsync(ReportRequest request, int comercioId, string planNivel);

    /// <summary>
    /// Exporta PDF con personalización (zona horaria local, branding con RazonSocial, componentes seleccionados).
    /// </summary>
    Task<byte[]> ExportarPdfAsync(ReportResult reporte, PdfExportContext contexto);

    /// <summary>
    /// Exports the given report as a PDF byte array (simple text-table format).
    /// Se mantiene por backward compatibility.
    /// </summary>
    Task<byte[]> ExportarPdfAsync(ReportResult reporte);

    /// <summary>
    /// Exports the given report as a CSV byte array.
    /// </summary>
    Task<byte[]> ExportarCsvAsync(ReportResult reporte);
}
