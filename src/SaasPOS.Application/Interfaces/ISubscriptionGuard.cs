namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Validates subscription plan limits before allowing entity creation or feature usage.
/// Fail-safe policy (Req 1.7): returns FALSE if limits cannot be determined.
/// </summary>
public interface ISubscriptionGuard
{
    /// <summary>
    /// Checks if a new user can be added to the comercio (or sucursal for Plan Intermedio).
    /// Plan Básico: max 2 users total per Comercio.
    /// Plan Intermedio: max 3 users per Sucursal.
    /// Plan Empresarial: unlimited.
    /// </summary>
    Task<bool> CanAddUserAsync(int comercioId, int? sucursalId = null);

    /// <summary>
    /// Checks if a new sucursal can be added to the comercio.
    /// Plan Básico: max 1 Sucursal.
    /// Plan Intermedio &amp; Empresarial: unlimited.
    /// </summary>
    Task<bool> CanAddSucursalAsync(int comercioId);

    /// <summary>
    /// Checks if a new attribute can be added to a category.
    /// Plan Básico: max 2 per Categoría.
    /// Plan Intermedio: max 5 per Categoría.
    /// Plan Empresarial: unlimited.
    /// </summary>
    Task<bool> CanAddAtributoCategoriaAsync(int comercioId, int categoriaId);

    /// <summary>
    /// Checks if a role is allowed for the comercio's current plan.
    /// Delegates to RolePlanMapping as the single source of truth:
    /// Plan Básico: Dueño, Cajero.
    /// Plan Intermedio: Dueño, Gerente, Cajero, Bodeguero.
    /// Plan Empresarial: Dueño, Gerente, Supervisor, Cajero, Bodeguero.
    /// SuperAdmin is excluded (platform role, not a commerce role).
    /// </summary>
    Task<bool> CanUseRoleAsync(int comercioId, string role);

    /// <summary>
    /// Checks if the comercio can generate reports.
    /// Plan Básico: blocked.
    /// Plan Intermedio &amp; Empresarial: allowed.
    /// </summary>
    Task<bool> CanGenerateReportAsync(int comercioId);

    /// <summary>
    /// Checks if the comercio can emit electronic invoices (factura electrónica).
    /// Requires UsaFacturacionSRI = true AND Plan != Básico.
    /// </summary>
    Task<bool> CanEmitirFacturaElectronicaAsync(int comercioId);

    /// <summary>
    /// Verifica si el comercio tiene acceso a funcionalidades de IA según su plan.
    /// Solo Plan Empresarial tiene acceso. Fail-safe: deniega en caso de error.
    /// </summary>
    Task<bool> CanUseAIAsync(int comercioId);
}
