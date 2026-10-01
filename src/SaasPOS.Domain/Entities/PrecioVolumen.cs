namespace SaasPOS.Domain.Entities;

public class PrecioVolumen
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public decimal CantidadMinima { get; set; }
    public decimal PrecioEspecial { get; set; }

    // Navigation
    public Producto Producto { get; set; } = null!;
}
