namespace SaasPOS.Application.Interfaces;

public interface ITenantContext
{
    int? ComercioId { get; }
    int? SucursalId { get; }
    bool IsSuperAdmin { get; }
}
