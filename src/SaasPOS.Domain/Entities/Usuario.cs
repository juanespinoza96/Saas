namespace SaasPOS.Domain.Entities;

public class Usuario
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public int? SucursalId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>SuperAdmin | Dueño | Gerente | Supervisor | Bodeguero | Cajero</summary>
    public string Rol { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;

    /// <summary>Identificador IANA de zona horaria preferida del usuario (ej: "America/Guayaquil"). Máximo 64 caracteres.</summary>
    public string? ZonaHoraria { get; set; }

    // NUEVO (Requirement 16.1): indica cambio de contraseña obligatorio
    public bool DebeCambiarPassword { get; set; } = false;

    // Navigation
    public Comercio Comercio { get; set; } = null!;
    public Sucursal? Sucursal { get; set; }
    public ICollection<Venta> Ventas { get; set; } = [];
    public ICollection<PagoComercio> PagosRegistrados { get; set; } = [];
    public ICollection<LogAuditoria> LogsAuditoria { get; set; } = [];
}
