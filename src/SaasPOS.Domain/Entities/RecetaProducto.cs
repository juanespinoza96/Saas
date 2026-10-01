namespace SaasPOS.Domain.Entities;

public class RecetaProducto
{
    public int Id { get; set; }
    public int ProductoFinalId { get; set; }
    public int IngredienteId { get; set; }
    public decimal CantidadRequerida { get; set; }

    // Navigation
    public Producto ProductoFinal { get; set; } = null!;
    public Producto Ingrediente { get; set; } = null!;
}
