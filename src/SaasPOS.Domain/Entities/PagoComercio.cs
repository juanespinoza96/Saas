namespace SaasPOS.Domain.Entities;

public class PagoComercio
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public int SuscripcionId { get; set; }
    public decimal MontoPagado { get; set; }
    public DateOnly FechaPago { get; set; }
    public string MetodoPago { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    public int RegistradoPor { get; set; }
    /// <summary>Tipo de pago: Mensualidad | Licencia.</summary>
    public string Tipo { get; set; } = "Mensualidad";
    /// <summary>Plan al que corresponde la licencia pagada. Nulo en las mensualidades.</summary>
    public int? PlanId { get; set; }

    // Navigation
    public Plan? Plan { get; set; }
    public Comercio Comercio { get; set; } = null!;
    public Suscripcion Suscripcion { get; set; } = null!;
    public Usuario UsuarioRegistrador { get; set; } = null!;
}
