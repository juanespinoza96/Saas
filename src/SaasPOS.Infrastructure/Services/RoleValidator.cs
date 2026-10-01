using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementación del validador de roles por plan de suscripción.
/// Usa IgnoreQueryFilters para validación a nivel de sistema (multi-tenant bypass).
/// Política fail-safe: ante cualquier error o estado indeterminado, denegar acceso.
/// </summary>
public class RoleValidator : IRoleValidator
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<RoleValidator> _logger;

    public RoleValidator(AppDbContext dbContext, ILogger<RoleValidator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RolDisponibleDto>> GetRolesDisponiblesAsync(int comercioId)
    {
        try
        {
            // Obtener el plan del comercio
            var nombrePlan = await ObtenerNombrePlanAsync(comercioId);

            if (nombrePlan is null)
            {
                _logger.LogWarning(
                    "No se pudo determinar el plan para ComercioId={ComercioId}. Retornando lista vacía (fail-safe).",
                    comercioId);
                return Array.Empty<RolDisponibleDto>();
            }

            // Obtener roles permitidos por el plan (excluye SuperAdmin por diseño de RolePlanMapping)
            var rolesDelPlan = RolePlanMapping.GetRolesParaPlan(nombrePlan);

            if (rolesDelPlan.Count == 0)
            {
                _logger.LogWarning(
                    "Plan '{NombrePlan}' no tiene roles definidos para ComercioId={ComercioId}.",
                    nombrePlan, comercioId);
                return Array.Empty<RolDisponibleDto>();
            }

            // Verificar si ya existe un Dueño en el comercio
            var existeDueno = await _dbContext.Usuarios
                .IgnoreQueryFilters()
                .AnyAsync(u => u.ComercioId == comercioId
                            && u.Rol == "Dueño"
                            && u.Activo);

            // Construir lista de roles disponibles excluyendo Dueño si ya existe
            var rolesDisponibles = rolesDelPlan
                .Where(rol => !existeDueno || !rol.Equals("Dueño", StringComparison.OrdinalIgnoreCase))
                .Select(rol => new RolDisponibleDto(rol, rol))
                .ToList();

            return rolesDisponibles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error obteniendo roles disponibles para ComercioId={ComercioId}. Fail-safe: retornando lista vacía.",
                comercioId);
            return Array.Empty<RolDisponibleDto>();
        }
    }

    /// <inheritdoc />
    public async Task<RoleValidationResult> ValidarRolParaComercioAsync(int comercioId, string rol)
    {
        try
        {
            // Validar que el rol sea reconocido por el sistema (400)
            if (!RolePlanMapping.EsRolValido(rol))
            {
                var rolesValidos = string.Join(", ", RolePlanMapping.GetTodosLosRolesValidos());
                return new RoleValidationResult(
                    EsValido: false,
                    MensajeError: $"El rol '{rol}' no es válido. Roles válidos: {rolesValidos}",
                    RolesDisponibles: null);
            }

            // Obtener el plan del comercio
            var nombrePlan = await ObtenerNombrePlanAsync(comercioId);

            if (nombrePlan is null)
            {
                _logger.LogWarning(
                    "No se pudo determinar el plan para ComercioId={ComercioId} al validar rol '{Rol}'. Fail-safe: denegando.",
                    comercioId, rol);
                return new RoleValidationResult(
                    EsValido: false,
                    MensajeError: "No se pudo verificar el plan del comercio",
                    RolesDisponibles: null);
            }

            // Validar que el rol esté permitido por el plan (403)
            if (!RolePlanMapping.EsRolPermitido(nombrePlan, rol))
            {
                var rolesPermitidos = RolePlanMapping.GetRolesParaPlan(nombrePlan)
                    .Where(r => !r.Equals("Dueño", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var listaRoles = string.Join(", ", rolesPermitidos);
                return new RoleValidationResult(
                    EsValido: false,
                    MensajeError: $"El rol '{rol}' no está permitido en el plan actual. Roles disponibles: {listaRoles}",
                    RolesDisponibles: rolesPermitidos);
            }

            return new RoleValidationResult(
                EsValido: true,
                MensajeError: null,
                RolesDisponibles: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error validando rol '{Rol}' para ComercioId={ComercioId}. Fail-safe: denegando.",
                rol, comercioId);
            return new RoleValidationResult(
                EsValido: false,
                MensajeError: "Error interno al validar el rol",
                RolesDisponibles: null);
        }
    }

    /// <inheritdoc />
    public async Task<bool> EsUnicoDuenoAsync(int comercioId, int usuarioId)
    {
        try
        {
            // Contar cuántos usuarios activos con rol Dueño hay en el comercio
            var cantidadDuenos = await _dbContext.Usuarios
                .IgnoreQueryFilters()
                .CountAsync(u => u.ComercioId == comercioId
                              && u.Rol == "Dueño"
                              && u.Activo);

            if (cantidadDuenos != 1)
                return false;

            // Verificar que el usuario dado sea ese único Dueño
            var esElDueno = await _dbContext.Usuarios
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Id == usuarioId
                            && u.ComercioId == comercioId
                            && u.Rol == "Dueño"
                            && u.Activo);

            return esElDueno;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error verificando unicidad de Dueño para ComercioId={ComercioId}, UsuarioId={UsuarioId}. Fail-safe: asumiendo que es único Dueño.",
                comercioId, usuarioId);
            // Fail-safe: ante duda, proteger al Dueño (asumir que es único)
            return true;
        }
    }

    /// <summary>
    /// Obtiene el nombre del plan asociado al comercio.
    /// Retorna null si no se puede determinar (comercio no encontrado, sin plan, etc.)
    /// </summary>
    private async Task<string?> ObtenerNombrePlanAsync(int comercioId)
    {
        var comercio = await _dbContext.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == comercioId);

        return comercio?.Plan?.Nombre;
    }
}
