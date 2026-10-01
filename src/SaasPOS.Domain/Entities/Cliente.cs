namespace SaasPOS.Domain.Entities;

public class Cliente
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public string Identificacion { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Correo { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public bool EsConsumidorFinal { get; set; }

    // Navigation
    public Comercio Comercio { get; set; } = null!;
    public ICollection<Venta> Ventas { get; set; } = [];
}
