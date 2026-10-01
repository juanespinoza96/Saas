namespace SaasPOS.Domain.Entities;

public class StockSucursal
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public int SucursalId { get; set; }
    public decimal CantidadFisica { get; set; }
    public decimal StockMinimo { get; set; }

    // Navigation
    public Producto Producto { get; set; } = null!;
    public Sucursal Sucursal { get; set; } = null!;
}
