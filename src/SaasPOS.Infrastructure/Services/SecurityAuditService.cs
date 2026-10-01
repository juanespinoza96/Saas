using Microsoft.Extensions.Logging;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementación del servicio de auditoría de eventos de seguridad (Req 3.4, 3.7).
/// Persiste eventos en la tabla LogsAuditoria con categoría "Seguridad".
/// Todos los métodos son fire-and-forget safe: capturan excepciones internamente
/// para evitar afectar el flujo principal de la solicitud.
/// </summary>
public class SecurityAuditService : ISecurityAuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SecurityAuditService> _logger;

    public SecurityAuditService(AppDbContext db, ILogger<SecurityAuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LogAuthenticationFailedAsync(string email, string ipAddress, string reason)
    {
        try
        {
            var log = new LogAuditoria
            {
                ComercioId = 0,
                UsuarioId = null,
                Accion = "AutenticacionFallida",
                FechaHora = DateTime.UtcNow,
                TablaAfectada = "Seguridad",
                RegistroId = "0",
                ValoresAnteriores = null,
                ValoresNuevos = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Email = email,
                    IpAddress = ipAddress,
                    Reason = reason,
                    Categoria = "Seguridad"
                })
            };

            _db.LogsAuditoria.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al registrar auditoría de autenticación fallida para {Email}", email);
        }
    }

    /// <inheritdoc />
    public async Task LogAccessViolationAsync(int? comercioId, int? usuarioId, string ipAddress, string path, string reason)
    {
        try
        {
            var log = new LogAuditoria
            {
                ComercioId = comercioId ?? 0,
                UsuarioId = usuarioId,
                Accion = "ViolacionAcceso",
                FechaHora = DateTime.UtcNow,
                TablaAfectada = "Seguridad",
                RegistroId = "0",
                ValoresAnteriores = null,
                ValoresNuevos = System.Text.Json.JsonSerializer.Serialize(new
                {
                    IpAddress = ipAddress,
                    Path = path,
                    Reason = reason,
                    Categoria = "Seguridad"
                })
            };

            _db.LogsAuditoria.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al registrar auditoría de violación de acceso en {Path}", path);
        }
    }

    /// <inheritdoc />
    public async Task LogInputValidationErrorAsync(string ipAddress, string path, string detectedType)
    {
        try
        {
            var log = new LogAuditoria
            {
                ComercioId = 0,
                UsuarioId = null,
                Accion = "ErrorValidacionEntrada",
                FechaHora = DateTime.UtcNow,
                TablaAfectada = "Seguridad",
                RegistroId = "0",
                ValoresAnteriores = null,
                ValoresNuevos = System.Text.Json.JsonSerializer.Serialize(new
                {
                    IpAddress = ipAddress,
                    Path = path,
                    DetectedType = detectedType,
                    Categoria = "Seguridad"
                })
            };

            _db.LogsAuditoria.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al registrar auditoría de validación de entrada desde {IP}", ipAddress);
        }
    }

    /// <inheritdoc />
    public async Task LogSecurityConfigChangeAsync(int comercioId, int usuarioId, string configuracion, object? valorAnterior, object? valorNuevo)
    {
        try
        {
            var log = new LogAuditoria
            {
                ComercioId = comercioId,
                UsuarioId = usuarioId,
                Accion = "CambioConfiguracionSeguridad",
                FechaHora = DateTime.UtcNow,
                TablaAfectada = "Seguridad",
                RegistroId = "0",
                ValoresAnteriores = valorAnterior is not null
                    ? System.Text.Json.JsonSerializer.Serialize(new { Configuracion = configuracion, Valor = valorAnterior, Categoria = "Seguridad" })
                    : null,
                ValoresNuevos = valorNuevo is not null
                    ? System.Text.Json.JsonSerializer.Serialize(new { Configuracion = configuracion, Valor = valorNuevo, Categoria = "Seguridad" })
                    : null
            };

            _db.LogsAuditoria.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al registrar auditoría de cambio de configuración de seguridad para comercio {ComercioId}", comercioId);
        }
    }

    /// <inheritdoc />
    public async Task LogCrossTenantAccessAsync(int atacanteComercioId, int? atacanteUsuarioId, string ipAddress, string path, int? targetComercioId)
    {
        try
        {
            var log = new LogAuditoria
            {
                ComercioId = atacanteComercioId,
                UsuarioId = atacanteUsuarioId,
                Accion = "AccesoCrossTenant",
                FechaHora = DateTime.UtcNow,
                TablaAfectada = "Seguridad",
                RegistroId = "0",
                ValoresAnteriores = null,
                ValoresNuevos = System.Text.Json.JsonSerializer.Serialize(new
                {
                    IpAddress = ipAddress,
                    Path = path,
                    AtacanteComercioId = atacanteComercioId,
                    TargetComercioId = targetComercioId,
                    Categoria = "Seguridad"
                })
            };

            _db.LogsAuditoria.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al registrar auditoría de acceso cross-tenant desde comercio {ComercioId}", atacanteComercioId);
        }
    }
}
