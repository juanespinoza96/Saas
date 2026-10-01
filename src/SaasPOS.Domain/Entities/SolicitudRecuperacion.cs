namespace SaasPOS.Domain.Entities;

public class SolicitudRecuperacion
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public int ComercioId { get; set; }
    /// <summary>Pendiente | Aprobada | Rechazada</summary>
    public string Estado { get; set; } = "Pendiente";
    public int? AprobadoPor { get; set; }
    public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;
    public DateTime? FechaResolucion { get; set; }

    // NUEVO (Requirement 16.2): contraseña temporal cifrada AES-256 (nullable)
    public string? PasswordTemporalCifrada { get; set; }
    // NUEVO (Requirement 16.2): expiración de la temporal (nullable)
    public DateTime? FechaExpiracion { get; set; }
    // NUEVO (Requirement 16.3): motivo de rechazo (nullable)
    public string? MotivoRechazo { get; set; }

    // Navigation
    public Usuario Usuario { get; set; } = null!;
    public Comercio Comercio { get; set; } = null!;
    public Usuario? Aprobador { get; set; }
}
