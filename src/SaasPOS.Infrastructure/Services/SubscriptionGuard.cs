using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Validates subscription plan limits. Uses IgnoreQueryFilters for system-level validation.
/// Fail-safe policy (Req 1.7): returns FALSE on any error or indeterminate state.
/// Restricciones adicionales para trials (Req 2.1-2.4): roles limitados a Gerente/Cajero,
/// reportes y SRI bloqueados cuando Estado == "Trial".
/// </summary>
public class SubscriptionGuard : ISubscriptionGuard
{
    private const string PlanBasico = "Básico";
    private const string PlanIntermedio = "Intermedio";
    private const string PlanEmpresarial = "Empresarial";
    private const string EstadoTrial = "Trial";

    /// <summary>Roles permitidos durante el período de trial (Req 2.3).</summary>
    private static readonly HashSet<string> RolesPermitidosEnTrial =
        new(StringComparer.OrdinalIgnoreCase) { "Gerente", "Cajero" };

    private readonly AppDbContext _dbContext;
    private readonly ILogger<SubscriptionGuard> _logger;

    public SubscriptionGuard(AppDbContext dbContext, ILogger<SubscriptionGuard> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene la suscripción activa (o en trial) más reciente de un comercio.
    /// Retorna null si no se encuentra ninguna.
    /// </summary>
    private async Task<Suscripcion?> GetSuscripcionActivaAsync(int comercioId)
    {
        return await _dbContext.Suscripciones
            .IgnoreQueryFilters()
            .Where(s => s.ComercioId == comercioId)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Verifica si el comercio tiene una suscripción en estado Trial.
    /// </summary>
    private async Task<bool> EstaEnTrialAsync(int comercioId)
    {
        var suscripcion = await GetSuscripcionActivaAsync(comercioId);
        return suscripcion?.Estado == EstadoTrial;
    }

    public async Task<bool> CanAddUserAsync(int comercioId, int? sucursalId = null)
    {
        try
        {
            var comercio = await _dbContext.Comercios
                .IgnoreQueryFilters()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == comercioId);

            if (comercio?.Plan is null)
                return false; // Fail-safe: cannot determine limits

            var planName = comercio.Plan.Nombre;

            if (planName == PlanEmpresarial)
                return true; // Unlimited

            if (planName == PlanBasico)
            {
                // Max 2 usuarios activos por Comercio — solo los activos cuentan contra el límite
                var currentUserCount = await _dbContext.Usuarios
                    .IgnoreQueryFilters()
                    .CountAsync(u => u.ComercioId == comercioId && u.Activo);

                return currentUserCount < comercio.Plan.LimiteUsuarios;
            }

            if (planName == PlanIntermedio)
            {
                // Max 3 users per Sucursal (1 Gerente + 2 Cajeros)
                if (sucursalId is null)
                {
                    // If no sucursalId provided, check total against plan limit
                    // The limit is per-sucursal, so we need the sucursalId
                    // Fail-safe: deny if we can't determine the specific sucursal
                    return false;
                }

                var currentUsersInSucursal = await _dbContext.Usuarios
                    .IgnoreQueryFilters()
                    .CountAsync(u => u.ComercioId == comercioId && u.SucursalId == sucursalId && u.Activo);

                return currentUsersInSucursal < comercio.Plan.LimiteUsuarios;
            }

            // Unknown plan — fail-safe: deny
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking CanAddUser for ComercioId={ComercioId}, SucursalId={SucursalId}. Fail-safe: denying.",
                comercioId, sucursalId);
            return false;
        }
    }

    public async Task<bool> CanAddSucursalAsync(int comercioId)
    {
        try
        {
            var comercio = await _dbContext.Comercios
                .IgnoreQueryFilters()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == comercioId);

            if (comercio?.Plan is null)
                return false; // Fail-safe

            var planName = comercio.Plan.Nombre;

            if (planName == PlanBasico)
            {
                // Max 1 Sucursal
                var currentCount = await _dbContext.Sucursales
                    .IgnoreQueryFilters()
                    .CountAsync(s => s.ComercioId == comercioId);

                return currentCount < 1;
            }

            // Intermedio & Empresarial: unlimited
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking CanAddSucursal for ComercioId={ComercioId}. Fail-safe: denying.", comercioId);
            return false;
        }
    }

    public async Task<bool> CanAddAtributoCategoriaAsync(int comercioId, int categoriaId)
    {
        try
        {
            var comercio = await _dbContext.Comercios
                .IgnoreQueryFilters()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == comercioId);

            if (comercio?.Plan is null)
                return false; // Fail-safe

            var planName = comercio.Plan.Nombre;

            if (planName == PlanEmpresarial)
                return true; // Unlimited

            var currentAttributeCount = await _dbContext.AtributosCategoria
                .IgnoreQueryFilters()
                .CountAsync(a => a.CategoriaId == categoriaId);

            var limit = comercio.Plan.LimiteAtributos;
            return currentAttributeCount < limit;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking CanAddAtributoCategoria for ComercioId={ComercioId}, CategoriaId={CategoriaId}. Fail-safe: denying.",
                comercioId, categoriaId);
            return false;
        }
    }

    public async Task<bool> CanUseRoleAsync(int comercioId, string role)
    {
        try
        {
            var comercio = await _dbContext.Comercios
                .IgnoreQueryFilters()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == comercioId);

            if (comercio?.Plan is null)
                return false; // Fail-safe: no se puede determinar el plan

            // Restricción de trial (Req 2.3): solo Gerente y Cajero permitidos
            if (await EstaEnTrialAsync(comercioId))
            {
                return RolesPermitidosEnTrial.Contains(role);
            }

            var planName = comercio.Plan.Nombre;

            // Delegar la validación de roles por plan a la fuente de verdad estática
            return RolePlanMapping.EsRolPermitido(planName, role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking CanUseRole for ComercioId={ComercioId}, Role={Role}. Fail-safe: denying.",
                comercioId, role);
            return false;
        }
    }

    public async Task<bool> CanGenerateReportAsync(int comercioId)
    {
        try
        {
            // Restricción de trial (Req 2.2): reportes bloqueados durante trial
            if (await EstaEnTrialAsync(comercioId))
                return false;

            var comercio = await _dbContext.Comercios
                .IgnoreQueryFilters()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == comercioId);

            if (comercio?.Plan is null)
                return false; // Fail-safe

            var planName = comercio.Plan.Nombre;

            // Plan Básico: blocked; Intermedio & Empresarial: allowed
            return planName != PlanBasico;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking CanGenerateReport for ComercioId={ComercioId}. Fail-safe: denying.", comercioId);
            return false;
        }
    }

    public async Task<bool> CanEmitirFacturaElectronicaAsync(int comercioId)
    {
        try
        {
            // Restricción de trial (Req 2.4): facturación electrónica SRI bloqueada durante trial
            if (await EstaEnTrialAsync(comercioId))
                return false;

            var comercio = await _dbContext.Comercios
                .IgnoreQueryFilters()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == comercioId);

            if (comercio?.Plan is null)
                return false; // Fail-safe

            var planName = comercio.Plan.Nombre;

            // Plan Básico: always blocked
            if (planName == PlanBasico)
                return false;

            // Must have UsaFacturacionSRI enabled
            return comercio.UsaFacturacionSRI;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking CanEmitirFacturaElectronica for ComercioId={ComercioId}. Fail-safe: denying.", comercioId);
            return false;
        }
    }

    /// <summary>
    /// Verifica si el comercio tiene acceso a funcionalidades de IA.
    /// Solo Plan Empresarial tiene acceso. Fail-safe: deniega en caso de error.
    /// </summary>
    public async Task<bool> CanUseAIAsync(int comercioId)
    {
        try
        {
            var comercio = await _dbContext.Comercios
                .IgnoreQueryFilters()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == comercioId);

            if (comercio?.Plan is null)
                return false; // Fail-safe: no se puede determinar el plan

            var planName = comercio.Plan.Nombre;

            // Solo Plan Empresarial tiene acceso a funcionalidades de IA
            return planName == PlanEmpresarial;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking CanUseAI for ComercioId={ComercioId}. Fail-safe: denying.", comercioId);
            return false;
        }
    }
}
