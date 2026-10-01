namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Servicio especializado en auditoría de eventos de seguridad (Req 3.4, 3.7).
/// Registra en LogsAuditoria con categoría "Seguridad" los siguientes tipos de evento:
/// - Intentos de autenticación fallidos
/// - Violaciones de control de acceso
/// - Errores de validación de entrada
/// - Cambios en configuración de seguridad
/// - Accesos cross-tenant no autorizados
/// </summary>
public interface ISecurityAuditService
{
    /// <summary>
    /// Registra un intento de autenticación fallido.
    /// </summary>
    Task LogAuthenticationFailedAsync(string email, string ipAddress, string reason);

    /// <summary>
    /// Registra una violación de control de acceso (403).
    /// </summary>
    Task LogAccessViolationAsync(int? comercioId, int? usuarioId, string ipAddress, string path, string reason);

    /// <summary>
    /// Registra un error de validación de entrada (inyección SQL/XSS detectada).
    /// </summary>
    Task LogInputValidationErrorAsync(string ipAddress, string path, string detectedType);

    /// <summary>
    /// Registra un cambio en la configuración de seguridad del sistema.
    /// </summary>
    Task LogSecurityConfigChangeAsync(int comercioId, int usuarioId, string configuracion, object? valorAnterior, object? valorNuevo);

    /// <summary>
    /// Registra un intento de acceso cross-tenant no autorizado (Req 3.7).
    /// El sistema retornará 403 sin revelar la existencia del recurso.
    /// </summary>
    Task LogCrossTenantAccessAsync(int atacanteComercioId, int? atacanteUsuarioId, string ipAddress, string path, int? targetComercioId);
}
