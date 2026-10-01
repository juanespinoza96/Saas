namespace SaasPOS.Domain.Entities;

public class Categoria
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public string Nombre { get; set; } = string.Empty;

    // Navigation
    public Comercio Comercio { get; set; } = null!;
    public ICollection<AtributoCategoria> Atributos { get; set; } = [];
    public ICollection<Producto> Productos { get; set; } = [];
}
