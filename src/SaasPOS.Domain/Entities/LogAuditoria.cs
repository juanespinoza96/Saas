namespace SaasPOS.Domain.Entities;

public class LogAuditoria
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    /// <summary>Nullable with ON DELETE SET NULL — preserved when user is deleted.</summary>
    public int? UsuarioId { get; set; }
    public string Accion { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
    public string TablaAfectada { get; set; } = string.Empty;
    public string RegistroId { get; set; } = string.Empty;
    /// <summary>JSONB column for previous values (stored as raw JSON string).</summary>
    public string? ValoresAnteriores { get; set; }
    /// <summary>JSONB column for new values (stored as raw JSON string).</summary>
    public string? ValoresNuevos { get; set; }

    // Navigation
    public Comercio Comercio { get; set; } = null!;
    public Usuario? Usuario { get; set; }
}
