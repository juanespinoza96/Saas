namespace SaasPOS.Domain.Entities;

public class Sucursal
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? SerieFacturacion { get; set; }

    // Navigation
    public Comercio Comercio { get; set; } = null!;
    public ConfiguracionSucursal? Configuracion { get; set; }
    public ICollection<Usuario> Usuarios { get; set; } = [];
    public ICollection<StockSucursal> Stocks { get; set; } = [];
    public ICollection<Venta> Ventas { get; set; } = [];
    public ICollection<Notificacion> Notificaciones { get; set; } = [];
}
