using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Services;

/// <summary>
/// Tenant context used for /api/admin/ endpoints.
/// IsSuperAdmin = true bypasses EF Core global query filters,
/// allowing cross-tenant data access for the SuperAdmin panel.
/// </summary>
public class AdminTenantContext : ITenantContext
{
    public int? ComercioId => null;
    public int? SucursalId => null;
    public bool IsSuperAdmin => true;
}
