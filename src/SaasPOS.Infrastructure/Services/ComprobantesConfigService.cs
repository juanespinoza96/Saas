using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementación del servicio de gestión de tipos de comprobante habilitados por comercio.
/// Valida invariantes de negocio: mínimo 1 tipo habilitado y prerrequisitos para Factura Electrónica.
/// </summary>
public class ComprobantesConfigService : IComprobantesConfigService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ComprobantesConfigService> _logger;

    /// <summary>
    /// Nombre del plan básico que no permite facturación electrónica.
    /// </summary>
    private const string PlanBasico = "Básico";

    public ComprobantesConfigService(
        AppDbContext db,
        ILogger<ComprobantesConfigService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<ConfiguracionComprobanteDto>> GetHabilitadosAsync(int comercioId)
    {
        var configuraciones = await _db.ConfiguracionesComprobante
            .Where(c => c.ComercioId == comercioId)
            .Select(c => new ConfiguracionComprobanteDto
            {
                TipoComprobante = c.TipoComprobante,
                Habilitado = c.Habilitado
            })
            .ToListAsync();

        return configuraciones;
    }

    /// <inheritdoc />
    public async Task<Result> HabilitarTipoAsync(int comercioId, string tipo)
    {
        // Normalizar el tipo antes de procesar
        var tipoNormalizado = NormalizarTipoComprobante(tipo);

        // Validar prerrequisitos para Factura Electrónica
        if (tipoNormalizado == "Factura Electrónica")
        {
            var comercio = await _db.Comercios
                .IgnoreQueryFilters()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == comercioId);

            if (comercio is null)
                return Result.Fail("Comercio no encontrado.");

            // Validar que el plan no sea Básico
            if (comercio.Plan.Nombre == PlanBasico)
            {
                _logger.LogWarning(
                    "Comercio {ComercioId} con Plan Básico intentó habilitar Factura Electrónica.",
                    comercioId);
                return Result.Fail(
                    "La facturación electrónica requiere Plan Intermedio o superior.",
                    "FACTURA_REQUIRES_PLAN");
            }

            // Validar que tenga configurada la firma digital del SRI
            if (!comercio.UsaFacturacionSRI)
            {
                _logger.LogWarning(
                    "Comercio {ComercioId} sin firma SRI intentó habilitar Factura Electrónica.",
                    comercioId);
                return Result.Fail(
                    "Se requiere configurar los datos de facturación SRI previamente.",
                    "FACTURA_REQUIRES_SRI_CONFIG");
            }
        }

        // Buscar la configuración existente
        var config = await _db.ConfiguracionesComprobante
            .FirstOrDefaultAsync(c => c.ComercioId == comercioId && c.TipoComprobante == tipoNormalizado);

        if (config is null)
        {
            // Crear nueva configuración habilitada
            _db.ConfiguracionesComprobante.Add(new Domain.Entities.ConfiguracionComprobante
            {
                ComercioId = comercioId,
                TipoComprobante = tipoNormalizado,
                Habilitado = true,
                FechaCreacion = DateTime.UtcNow
            });
        }
        else
        {
            config.Habilitado = true;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Tipo de comprobante '{Tipo}' habilitado para Comercio {ComercioId}.",
            tipoNormalizado, comercioId);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result> DeshabilitarTipoAsync(int comercioId, string tipo)
    {
        var tipoNormalizado = NormalizarTipoComprobante(tipo);

        // Contar cuántos tipos están actualmente habilitados
        var habilitadosCount = await _db.ConfiguracionesComprobante
            .CountAsync(c => c.ComercioId == comercioId && c.Habilitado);

        // Verificar que el tipo que se quiere deshabilitar esté actualmente habilitado
        var config = await _db.ConfiguracionesComprobante
            .FirstOrDefaultAsync(c => c.ComercioId == comercioId && c.TipoComprobante == tipoNormalizado);

        if (config is null || !config.Habilitado)
        {
            // El tipo ya está deshabilitado o no existe, no hay nada que hacer
            return Result.Ok();
        }

        // Validar invariante: mínimo 1 tipo habilitado
        if (habilitadosCount <= 1)
        {
            _logger.LogWarning(
                "Comercio {ComercioId} intentó deshabilitar el último tipo de comprobante '{Tipo}'.",
                comercioId, tipoNormalizado);
            return Result.Fail(
                "Debe mantener al menos un tipo de comprobante habilitado.",
                "COMPROBANTE_MIN_ONE_REQUIRED");
        }

        config.Habilitado = false;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Tipo de comprobante '{Tipo}' deshabilitado para Comercio {ComercioId}.",
            tipoNormalizado, comercioId);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<bool> EsTipoHabilitadoAsync(int comercioId, string tipo)
    {
        var tipoNormalizado = NormalizarTipoComprobante(tipo);

        return await _db.ConfiguracionesComprobante
            .AnyAsync(c => c.ComercioId == comercioId
                        && c.TipoComprobante == tipoNormalizado
                        && c.Habilitado);
    }

    /// <inheritdoc />
    public string NormalizarTipoComprobante(string tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo))
            return tipo;

        return tipo switch
        {
            "Ticket Interno" => "Ticket Digital",
            "Factura Electronica" => "Factura Electrónica",
            _ => tipo
        };
    }
}
