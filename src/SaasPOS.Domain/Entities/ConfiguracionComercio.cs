namespace SaasPOS.Domain.Entities;

public class ConfiguracionComercio
{
    public int ComercioId { get; set; }
    public bool EsBarEscolar { get; set; }
    public bool MostrarBotonCliente { get; set; }
    public bool PermiteVentaEnNegativo { get; set; }
    public bool ImpresionAutomaticaTicket { get; set; }

    // Navigation
    public Comercio Comercio { get; set; } = null!;
}
