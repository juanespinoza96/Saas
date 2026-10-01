using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Services;

/// <summary>
/// Scoped service that acts as the ITenantContext implementation.
/// By default, delegates to TenantContext (JWT-based). 
/// When the TenantContextMiddleware detects an /api/admin/ route,
/// it sets the Override to AdminTenantContext, which bypasses query filters.
/// </summary>
public class TenantContextAccessor : ITenantContext
{
    private readonly TenantContext _defaultContext;

    public TenantContextAccessor(TenantContext defaultContext)
    {
        _defaultContext = defaultContext;
    }

    /// <summary>
    /// When set, this overrides the default tenant context.
    /// Used by TenantContextMiddleware to inject AdminTenantContext for admin routes.
    /// </summary>
    public ITenantContext? Override { get; set; }

    public int? ComercioId => (Override ?? _defaultContext).ComercioId;

    public int? SucursalId => (Override ?? _defaultContext).SucursalId;

    public bool IsSuperAdmin => (Override ?? _defaultContext).IsSuperAdmin;
}
