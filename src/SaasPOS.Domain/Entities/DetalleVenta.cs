namespace SaasPOS.Domain.Entities;

public class DetalleVenta
{
    public int Id { get; set; }
    public int VentaId { get; set; }
    public int ProductoId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioRealCobrado { get; set; }

    // Navigation
    public Venta Venta { get; set; } = null!;
    public Producto Producto { get; set; } = null!;
}
