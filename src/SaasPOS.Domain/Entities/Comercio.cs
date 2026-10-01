namespace SaasPOS.Domain.Entities;

public class Comercio
{
    public int Id { get; set; }
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public int PlanId { get; set; }
    public bool UsaFacturacionSRI { get; set; }
    public string? RutaFirmaElectronica { get; set; }
    /// <summary>Stored AES-256 encrypted. Key lives in SRI_ENCRYPTION_KEY env var.</summary>
    public string? ClaveFirmaEncriptada { get; set; }
    /// <summary>Activo | Suspendido</summary>
    public string Estado { get; set; } = "Activo";
    public DateTime FechaRegistro { get; set; }

    /// <summary>Zona horaria IANA por defecto del comercio (ej: "America/Guayaquil"). Máximo 50 caracteres.</summary>
    public string? ZonaHorariaDefecto { get; set; }

    /// <summary>Convenience property — maps to Estado for backward compatibility. Not mapped to DB.</summary>
    public bool Activo
    {
        get => Estado == "Activo";
        set => Estado = value ? "Activo" : "Suspendido";
    }

    // Navigation
    public Plan Plan { get; set; } = null!;
    public ConfiguracionComercio? Configuracion { get; set; }
    public ICollection<Sucursal> Sucursales { get; set; } = [];
    public ICollection<Usuario> Usuarios { get; set; } = [];
    public ICollection<Categoria> Categorias { get; set; } = [];
    public ICollection<Producto> Productos { get; set; } = [];
    public ICollection<Cliente> Clientes { get; set; } = [];
    public ICollection<Venta> Ventas { get; set; } = [];
    public ICollection<Suscripcion> Suscripciones { get; set; } = [];
    public ICollection<PagoComercio> Pagos { get; set; } = [];
    public ICollection<Notificacion> Notificaciones { get; set; } = [];
    public ICollection<LogAuditoria> LogsAuditoria { get; set; } = [];
    public ICollection<ConfiguracionComprobante> ConfiguracionesComprobante { get; set; } = [];
}
