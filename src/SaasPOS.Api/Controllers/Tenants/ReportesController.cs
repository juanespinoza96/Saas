using Microsoft.AspNetCore.Authorization;
using SaasPOS.Api.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Api.Controllers.Tenants;

/// <summary>
/// Reportes endpoint. Plan-level access:
/// - Plan BÃƒÂ¡sico Ã¢â€ â€™ HTTP 403
/// - Plan Intermedio Ã¢â€ â€™ 3 predefined reports (current month, no filters)
/// - Plan Empresarial Ã¢â€ â€™ Predefined + custom reports with filters
/// Req 13.1Ã¢â‚¬â€œ13.5.
/// </summary>
[ApiController]
[Route("api/tenants/reportes")]
[Authorize(Policy = "CanViewReports")]
public class ReportesController : ControllerBase
{
    private readonly IReportEngine _reportEngine;
    private readonly ITenantContext _tenantContext;
    private readonly AppDbContext _db;
    private readonly ITimezoneService _timezoneService;

    public ReportesController(IReportEngine reportEngine, ITenantContext tenantContext, AppDbContext db, ITimezoneService timezoneService)
    {
        _reportEngine = reportEngine;
        _tenantContext = tenantContext;
        _db = db;
        _timezoneService = timezoneService;
    }

    /// <summary>
    /// GET /api/tenants/reportes/top-producto Ã¢â‚¬â€ Product with most quantity sold (Intermedio+).
    /// </summary>
    [HttpGet("top-producto")]
    public async Task<IActionResult> TopProducto()
    {
        var (comercioId, planNivel) = await GetComercioAndPlan();

        var request = new ReportRequest(TipoReporte.TopProducto);

        try
        {
            var result = await _reportEngine.GenerarReporteAsync(request, comercioId, planNivel);
            return Ok(ToResponse(result));
        }
        catch (ReportForbiddenException ex)
        {
            return StatusCode(403, new { error = ex.Message, code = "REPORT_PLAN_FORBIDDEN" });
        }
    }

    /// <summary>
    /// GET /api/tenants/reportes/top-categoria Ã¢â‚¬â€ Category with highest sales amount (Intermedio+).
    /// </summary>
    [HttpGet("top-categoria")]
    public async Task<IActionResult> TopCategoria()
    {
        var (comercioId, planNivel) = await GetComercioAndPlan();

        var request = new ReportRequest(TipoReporte.TopCategoria);

        try
        {
            var result = await _reportEngine.GenerarReporteAsync(request, comercioId, planNivel);
            return Ok(ToResponse(result));
        }
        catch (ReportForbiddenException ex)
        {
            return StatusCode(403, new { error = ex.Message, code = "REPORT_PLAN_FORBIDDEN" });
        }
    }

    /// <summary>
    /// GET /api/tenants/reportes/top-sucursal Ã¢â‚¬â€ Branch with highest total amount (Intermedio+).
    /// </summary>
    [HttpGet("top-sucursal")]
    public async Task<IActionResult> TopSucursal()
    {
        var (comercioId, planNivel) = await GetComercioAndPlan();

        var request = new ReportRequest(TipoReporte.TopSucursal);

        try
        {
            var result = await _reportEngine.GenerarReporteAsync(request, comercioId, planNivel);
            return Ok(ToResponse(result));
        }
        catch (ReportForbiddenException ex)
        {
            return StatusCode(403, new { error = ex.Message, code = "REPORT_PLAN_FORBIDDEN" });
        }
    }

    /// <summary>
    /// GET /api/tenants/reportes/personalizado Ã¢â‚¬â€ Custom report with filters (Empresarial only).
    /// </summary>
    [HttpGet("personalizado")]
    public async Task<IActionResult> Personalizado(
        [FromQuery] DateTime? fechaDesde,
        [FromQuery] DateTime? fechaHasta,
        [FromQuery] int? sucursalId,
        [FromQuery] int? categoriaId,
        [FromQuery] int? productoId,
        [FromQuery] string? metodoPago)
    {
        var (comercioId, planNivel) = await GetComercioAndPlan();

        // Normalizar fechas de filtro a UTC usando la zona horaria del usuario
        var fechaDesdeUtc = HttpContext.NormalizarFiltroFechaAUtc(fechaDesde, _timezoneService);
        var fechaHastaUtc = HttpContext.NormalizarFiltroFechaHastaAUtc(fechaHasta, _timezoneService);

        var request = new ReportRequest(
            TipoReporte: TipoReporte.Personalizado,
            FechaDesde: fechaDesdeUtc,
            FechaHasta: fechaHastaUtc,
            SucursalId: sucursalId,
            CategoriaId: categoriaId,
            ProductoId: productoId,
            MetodoPago: metodoPago);

        try
        {
            var result = await _reportEngine.GenerarReporteAsync(request, comercioId, planNivel);
            return Ok(ToResponse(result));
        }
        catch (ReportForbiddenException ex)
        {
            return StatusCode(403, new { error = ex.Message, code = "REPORT_PLAN_FORBIDDEN" });
        }
    }

    /// <summary>
    /// GET /api/tenants/reportes/exportar?tipo={tipo}&formato={pdf|csv} Ã¢â‚¬â€ Export report.
    /// Optionally accepts filter query params for Empresarial plan.
    /// </summary>
    [HttpGet("exportar")]
    public async Task<IActionResult> Exportar(
        [FromQuery] TipoReporte tipo,
        [FromQuery] FormatoExportacion formato,
        [FromQuery] DateTime? fechaDesde,
        [FromQuery] DateTime? fechaHasta,
        [FromQuery] int? sucursalId,
        [FromQuery] int? categoriaId,
        [FromQuery] int? productoId,
        [FromQuery] string? metodoPago,
        [FromQuery] bool? incluirGraficoBarras = null,
        [FromQuery] bool? incluirGraficoPastel = null,
        [FromQuery] bool? incluirTabla = null)
    {
        var comercioInfo = await GetComercioInfo();

        // Normalizar fechas de filtro a UTC usando la zona horaria del usuario
        var fechaDesdeUtc = HttpContext.NormalizarFiltroFechaAUtc(fechaDesde, _timezoneService);
        var fechaHastaUtc = HttpContext.NormalizarFiltroFechaHastaAUtc(fechaHasta, _timezoneService);

        var request = new ReportRequest(
            TipoReporte: tipo,
            FechaDesde: fechaDesdeUtc,
            FechaHasta: fechaHastaUtc,
            SucursalId: sucursalId,
            CategoriaId: categoriaId,
            ProductoId: productoId,
            MetodoPago: metodoPago);

        try
        {
            var result = await _reportEngine.GenerarReporteAsync(request, comercioInfo.ComercioId, comercioInfo.PlanNivel);

            byte[] fileBytes;
            string contentType;
            string fileName;

            if (formato == FormatoExportacion.Pdf)
            {
                // Resolver opciones de componentes según plan
                var componentesResult = ResolverComponentesPdf(
                    comercioInfo.PlanNivel, incluirGraficoBarras, incluirGraficoPastel, incluirTabla);

                if (componentesResult.Error != null)
                    return componentesResult.Error;

                // Construir contexto de exportación PDF con branding y zona horaria
                var contexto = new PdfExportContext(
                    RazonSocial: comercioInfo.RazonSocial,
                    ZonaHoraria: comercioInfo.ZonaHoraria,
                    Componentes: componentesResult.Opciones!);

                fileBytes = await _reportEngine.ExportarPdfAsync(result, contexto);
                contentType = "application/pdf";
                fileName = $"reporte_{tipo}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
            }
            else
            {
                // CSV ignora completamente los parámetros de personalización visual
                fileBytes = await _reportEngine.ExportarCsvAsync(result);
                contentType = "text/csv";
                fileName = $"reporte_{tipo}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            }

            return File(fileBytes, contentType, fileName);
        }
        catch (ReportForbiddenException ex)
        {
            return StatusCode(403, new { error = ex.Message, code = "REPORT_PLAN_FORBIDDEN" });
        }
    }

    // Ã¢â€â‚¬Ã¢â€â‚¬ Private helpers Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

    private int GetComercioId() =>
        _tenantContext.ComercioId
            ?? throw new UnauthorizedAccessException("ComercioId not available in tenant context.");

    private async Task<(int comercioId, string planNivel)> GetComercioAndPlan()
    {
        var comercioId = GetComercioId();

        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == comercioId);

        if (comercio?.Plan is null)
            throw new UnauthorizedAccessException("No se pudo determinar el plan del comercio.");

        return (comercioId, comercio.Plan.Nombre);
    }

    /// <summary>
    /// Obtiene información completa del comercio: Id, Plan, RazonSocial y zona horaria.
    /// </summary>
    private async Task<ComercioExportInfo> GetComercioInfo()
    {
        var comercioId = GetComercioId();

        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == comercioId);

        if (comercio?.Plan is null)
            throw new UnauthorizedAccessException("No se pudo determinar el plan del comercio.");

        return new ComercioExportInfo(
            ComercioId: comercioId,
            PlanNivel: comercio.Plan.Nombre,
            RazonSocial: comercio.RazonSocial,
            ZonaHoraria: comercio.ZonaHorariaDefecto ?? "America/Guayaquil");
    }

    /// <summary>
    /// Resuelve las opciones de componentes PDF según el plan del comercio.
    /// - Empresarial: respeta params (ninguno enviado → todos true; todos false → error 400)
    /// - Intermedio: estructura fija (tabla + barras, sin pastel)
    /// </summary>
    private (PdfComponentOptions? Opciones, IActionResult? Error) ResolverComponentesPdf(
        string planNivel, bool? incluirGraficoBarras, bool? incluirGraficoPastel, bool? incluirTabla)
    {
        if (string.Equals(planNivel, "Empresarial", StringComparison.OrdinalIgnoreCase))
        {
            // Si ningún parámetro fue enviado → todos los componentes activos
            if (incluirGraficoBarras is null && incluirGraficoPastel is null && incluirTabla is null)
            {
                return (new PdfComponentOptions(true, true, true), null);
            }

            // Al menos un parámetro enviado → usar valores explícitos (null = false)
            var barras = incluirGraficoBarras ?? false;
            var pastel = incluirGraficoPastel ?? false;
            var tabla = incluirTabla ?? false;

            // Validar que al menos un componente esté activo
            if (!barras && !pastel && !tabla)
            {
                var error = BadRequest(new { error = "Debe seleccionar al menos un componente para exportar", code = "PDF_NO_COMPONENTS" });
                return (null, error);
            }

            return (new PdfComponentOptions(barras, pastel, tabla), null);
        }

        // Plan Intermedio (u otro): estructura fija — tabla + barras, sin pastel
        return (new PdfComponentOptions(
            IncluirGraficoBarras: true,
            IncluirGraficoPastel: false,
            IncluirTabla: true), null);
    }

    /// <summary>
    /// DTO interno para transportar datos del comercio necesarios para la exportación.
    /// </summary>
    private record ComercioExportInfo(
        int ComercioId,
        string PlanNivel,
        string RazonSocial,
        string ZonaHoraria);

    private static ReporteResponse ToResponse(ReportResult result) =>
        new(
            TipoReporte: result.TipoReporte.ToString(),
            Titulo: result.Titulo,
            Datos: result.Datos,
            GeneradoEn: result.GeneradoEn);
}
