using SaasPOS.Application.DTOs;

namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Manages per-branch configuration (ConfiguracionesSucursal).
/// </summary>
public interface IConfiguracionSucursalService
{
    /// <summary>
    /// Obtiene la configuración de la sucursal. Si no existe, la crea con valores por defecto (self-healing).
    /// </summary>
    /// <param name="sucursalId">The branch identifier.</param>
    /// <param name="comercioId">The commerce identifier for ownership validation.</param>
    Task<ConfiguracionSucursalDto> GetBySucursalIdAsync(int sucursalId, int comercioId);

    /// <summary>
    /// Actualiza los 6 toggles de configuración. Valida pertenencia al comercio.
    /// Registra auditoría en la misma transacción.
    /// </summary>
    /// <param name="sucursalId">The branch identifier.</param>
    /// <param name="comercioId">The commerce identifier for ownership validation.</param>
    /// <param name="usuarioId">The user performing the update (for audit).</param>
    /// <param name="request">The 6 boolean toggle values to set.</param>
    Task<ConfiguracionSucursalDto> UpdateAsync(int sucursalId, int comercioId, int usuarioId, UpdateConfiguracionSucursalRequest request);

    /// <summary>
    /// Crea configuración con valores por defecto para una nueva sucursal.
    /// Llamado dentro de la transacción de creación de sucursal.
    /// </summary>
    /// <param name="sucursalId">The branch identifier.</param>
    Task CrearDefaultAsync(int sucursalId);
}
