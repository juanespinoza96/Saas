namespace SaasPOS.Domain.Entities;

public class ConfiguracionComercio
{
    public int ComercioId { get; set; }
    public bool EsBarEscolar { get; set; }
    public bool MostrarBotonCliente { get; set; }
    /// <summary>
    /// Sin uso: el interruptor "vender en negativo" que gobierna el stock es
    /// ConfiguracionSucursal.PermiteVentaEnNegativo. Se conserva la columna para no requerir migración.
    /// </summary>
    public bool PermiteVentaEnNegativo { get; set; }
    public bool ImpresionAutomaticaTicket { get; set; }

    // Navigation
    public Comercio Comercio { get; set; } = null!;
}
