namespace SaasPOS.Domain.Entities;

public class Plan
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public int LimiteUsuarios { get; set; }
    public int LimiteAtributos { get; set; }
    public int LimiteSucursales { get; set; }

    // Navigation
    public ICollection<Comercio> Comercios { get; set; } = [];
    public ICollection<Suscripcion> Suscripciones { get; set; } = [];
}
