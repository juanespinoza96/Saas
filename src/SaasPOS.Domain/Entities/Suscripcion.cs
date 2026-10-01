namespace SaasPOS.Domain.Entities;

public class Suscripcion
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public int PlanId { get; set; }
    public DateOnly FechaInicio { get; set; }
    public DateOnly FechaProximoCorte { get; set; }
    public decimal MontoCuota { get; set; }
    public bool EsProporcional { get; set; }
    /// <summary>Activa | Por vencer | En mora | Suspendido | Trial | Trial_Expirado</summary>
    public string Estado { get; set; } = "Activa";
    public DateOnly? FechaUltimoPago { get; set; }

    /// <summary>Acumulado de días de extensión otorgados al trial (max 30).</summary>
    public int DiasExtendidos { get; set; } = 0;

    // Navigation
    public Comercio Comercio { get; set; } = null!;
    public Plan Plan { get; set; } = null!;
    public ICollection<PagoComercio> Pagos { get; set; } = [];
}
