namespace SaasPOS.Domain.Entities;

public class AtributoCategoria
{
    public int Id { get; set; }
    public int CategoriaId { get; set; }
    public string NombreAtributo { get; set; } = string.Empty;
    /// <summary>Texto | Numero | Booleano | Fecha</summary>
    public string TipoDato { get; set; } = "Texto";

    // Navigation
    public Categoria Categoria { get; set; } = null!;
}
