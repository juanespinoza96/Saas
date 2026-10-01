namespace SaasPOS.Domain.Entities;

/// <summary>
/// Configuración de tipos de comprobante habilitados por comercio.
/// Cada comercio puede tener habilitados: Ticket Digital, Ticket Impreso, Factura Electrónica.
/// </summary>
public class ConfiguracionComprobante
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    /// <summary>Ticket Digital | Ticket Impreso | Factura Electrónica</summary>
    public string TipoComprobante { get; set; } = string.Empty;
    public bool Habilitado { get; set; } = true;
    public DateTime FechaCreacion { get; set; }

    // Navigation
    public Comercio Comercio { get; set; } = null!;
}
