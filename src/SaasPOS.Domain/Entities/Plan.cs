namespace SaasPOS.Domain.Entities;

public class Plan
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Licencia del plan (pago único).</summary>
    public decimal Precio { get; set; }
    /// <summary>Mensualidad del plan (cobro mensual con corte el día 3).</summary>
    public decimal CuotaMensual { get; set; }
    public int LimiteUsuarios { get; set; }
    public int LimiteAtributos { get; set; }
    public int LimiteSucursales { get; set; }

    // Navigation
    public ICollection<Comercio> Comercios { get; set; } = [];
    public ICollection<Suscripcion> Suscripciones { get; set; } = [];
}
