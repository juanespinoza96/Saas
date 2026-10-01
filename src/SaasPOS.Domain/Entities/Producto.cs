namespace SaasPOS.Domain.Entities;

public class Producto
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public int? CategoriaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Venta Directa | Insumo | Ensamblado</summary>
    public string TipoArticulo { get; set; } = "Venta Directa";
    /// <summary>JSONB column for dynamic category attributes (stored as raw JSON string).</summary>
    public string? ValoresDinamicos { get; set; }
    public bool ManejaStock { get; set; } = true;
    public string? UnidadMedida { get; set; }
    public decimal CostoProduccion { get; set; }
    public decimal PrecioLista { get; set; }
    public decimal PrecioMinimo { get; set; }

    // Navigation
    public Comercio Comercio { get; set; } = null!;
    public Categoria? Categoria { get; set; }
    public ICollection<RecetaProducto> RecetasComoFinal { get; set; } = [];
    public ICollection<RecetaProducto> RecetasComoIngrediente { get; set; } = [];
    public ICollection<PrecioVolumen> PreciosVolumen { get; set; } = [];
    public ICollection<StockSucursal> Stocks { get; set; } = [];
    public ICollection<DetalleVenta> DetalleVentas { get; set; } = [];
    public ICollection<Notificacion> Notificaciones { get; set; } = [];
}
