namespace SaasPOS.Domain.Entities;

public class Notificacion
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public int? SucursalId { get; set; }
    public int? ProductoId { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public bool Leida { get; set; }
    public string TipoNotificacion { get; set; } = string.Empty;
    public DateTime FechaEmision { get; set; }

    /// <summary>Referencia opcional a Suscripcion para verificación de duplicados en trials.</summary>
    public int? SuscripcionId { get; set; }

    // Navigation
    public Comercio Comercio { get; set; } = null!;
    public Sucursal? Sucursal { get; set; }
    public Producto? Producto { get; set; }
    public Suscripcion? Suscripcion { get; set; }
}
