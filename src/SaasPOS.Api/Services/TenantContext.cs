using System.Security.Claims;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Services;

public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? ComercioId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirstValue("comercio_id");
            if (int.TryParse(claim, out var id))
                return id;
            return null;
        }
    }

    public int? SucursalId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirstValue("sucursal_id");
            if (int.TryParse(claim, out var id))
                return id;
            return null;
        }
    }

    public bool IsSuperAdmin =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role) == "SuperAdmin"
        || _httpContextAccessor.HttpContext?.User?.FindFirstValue("role") == "SuperAdmin";
}
