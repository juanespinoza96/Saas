using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Manages per-branch configuration (ConfiguracionesSucursal).
/// Implements self-healing reads, transactional updates with audit, and default creation.
/// </summary>
public class ConfiguracionSucursalService : IConfiguracionSucursalService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _auditService;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly ILogger<ConfiguracionSucursalService> _logger;

    public ConfiguracionSucursalService(
        AppDbContext db,
        IAuditService auditService,
        ISecurityAuditService securityAuditService,
        ILogger<ConfiguracionSucursalService> logger)
    {
        _db = db;
        _auditService = auditService;
        _securityAuditService = securityAuditService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConfiguracionSucursalDto> GetBySucursalIdAsync(int sucursalId, int comercioId)
    {
        // Validate that the sucursal exists and belongs to the comercio
        var sucursal = await _db.Sucursales
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sucursalId && s.ComercioId == comercioId);

        if (sucursal is null)
            throw new KeyNotFoundException($"Sucursal {sucursalId} no encontrada o no pertenece al comercio.");

        // Try to read existing configuration
        var config = await _db.ConfiguracionesSucursal
            .FirstOrDefaultAsync(c => c.SucursalId == sucursalId);

        if (config is null)
        {
            // Self-healing: INSERT with defaults using ON CONFLICT DO NOTHING
            _logger.LogInformation(
                "ConfiguracionSucursal no encontrada para Sucursal {SucursalId}. Ejecutando self-healing.",
                sucursalId);

            await _db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""ConfiguracionesSucursal"" (""SucursalId"", ""EsBarEscolar"", ""MostrarBotonCliente"", ""PermiteVentaEnNegativo"", ""ImpresionAutomaticaTicket"", ""PermitePrecioNegociado"", ""MostrarVentasAlCajero"")
                  VALUES ({0}, FALSE, FALSE, FALSE, TRUE, FALSE, FALSE)
                  ON CONFLICT (""SucursalId"") DO NOTHING",
                sucursalId);

            // Re-read the record (either we just created it, or another process did)
            config = await _db.ConfiguracionesSucursal
                .FirstOrDefaultAsync(c => c.SucursalId == sucursalId);

            if (config is null)
                throw new InvalidOperationException(
                    $"No se pudo obtener ni crear la configuración para Sucursal {sucursalId}.");
        }

        return MapToDto(config);
    }

    /// <inheritdoc />
    public async Task<ConfiguracionSucursalDto> UpdateAsync(
        int sucursalId, int comercioId, int usuarioId, UpdateConfiguracionSucursalRequest request)
    {
        // Begin explicit transaction
        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // Validate sucursal belongs to comercio
            var sucursal = await _db.Sucursales
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sucursalId && s.ComercioId == comercioId);

            if (sucursal is null)
                throw new KeyNotFoundException($"Sucursal {sucursalId} no encontrada o no pertenece al comercio.");

            // SELECT FOR UPDATE to lock the row
            var configs = await _db.ConfiguracionesSucursal
                .FromSqlRaw(
                    @"SELECT * FROM ""ConfiguracionesSucursal"" WHERE ""SucursalId"" = {0} FOR UPDATE",
                    sucursalId)
                .ToListAsync();

            var config = configs.FirstOrDefault();

            if (config is null)
                throw new KeyNotFoundException(
                    $"Configuración para Sucursal {sucursalId} no encontrada.");

            // Capture old values for audit
            var valoresAnteriores = new
            {
                config.EsBarEscolar,
                config.MostrarBotonCliente,
                config.PermiteVentaEnNegativo,
                config.ImpresionAutomaticaTicket,
                config.PermitePrecioNegociado,
                config.MostrarVentasAlCajero
            };

            // Update the 6 boolean fields
            config.EsBarEscolar = request.EsBarEscolar;
            config.MostrarBotonCliente = request.MostrarBotonCliente;
            config.PermiteVentaEnNegativo = request.PermiteVentaEnNegativo;
            config.ImpresionAutomaticaTicket = request.ImpresionAutomaticaTicket;
            config.PermitePrecioNegociado = request.PermitePrecioNegociado;
            config.MostrarVentasAlCajero = request.MostrarVentasAlCajero;

            await _db.SaveChangesAsync();

            // Capture new values for audit
            var valoresNuevos = new
            {
                config.EsBarEscolar,
                config.MostrarBotonCliente,
                config.PermiteVentaEnNegativo,
                config.ImpresionAutomaticaTicket,
                config.PermitePrecioNegociado,
                config.MostrarVentasAlCajero
            };

            // INSERT audit log (participates in the ambient transaction)
            await _auditService.RegistrarAsync(
                comercioId,
                usuarioId,
                "Actualizar",
                "ConfiguracionesSucursal",
                sucursalId.ToString(),
                valoresAnteriores,
                valoresNuevos);

            // Req 3.4: Si cambió MostrarVentasAlCajero, registrar como cambio de configuración de seguridad
            if (valoresAnteriores.MostrarVentasAlCajero != config.MostrarVentasAlCajero)
            {
                await _securityAuditService.LogSecurityConfigChangeAsync(
                    comercioId,
                    usuarioId,
                    "MostrarVentasAlCajero",
                    valorAnterior: valoresAnteriores.MostrarVentasAlCajero,
                    valorNuevo: config.MostrarVentasAlCajero);
            }

            // COMMIT transaction (config update + audit log together)
            await transaction.CommitAsync();

            return MapToDto(config);
        }
        catch (Exception ex)
        {
            // Rollback entire transaction if anything fails (including audit)
            _logger.LogError(ex,
                "Error al actualizar ConfiguracionSucursal para Sucursal {SucursalId}. Rollback ejecutado.",
                sucursalId);
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CrearDefaultAsync(int sucursalId)
    {
        var config = new ConfiguracionSucursal
        {
            SucursalId = sucursalId,
            EsBarEscolar = false,
            MostrarBotonCliente = false,
            PermiteVentaEnNegativo = false,
            ImpresionAutomaticaTicket = true,
            PermitePrecioNegociado = false,
            MostrarVentasAlCajero = false
        };

        _db.ConfiguracionesSucursal.Add(config);
        await _db.SaveChangesAsync();
    }

    private static ConfiguracionSucursalDto MapToDto(ConfiguracionSucursal config)
    {
        return new ConfiguracionSucursalDto(
            config.SucursalId,
            config.EsBarEscolar,
            config.MostrarBotonCliente,
            config.PermiteVentaEnNegativo,
            config.ImpresionAutomaticaTicket,
            config.PermitePrecioNegociado,
            config.MostrarVentasAlCajero);
    }
}
