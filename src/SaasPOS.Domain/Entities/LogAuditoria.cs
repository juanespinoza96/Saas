namespace SaasPOS.Domain.Entities;

public class LogAuditoria
{
    public int Id { get; set; }
    /// <summary>
    /// Nullable con ON DELETE SET NULL. Los eventos de seguridad a nivel de sistema
    /// (login fallido, violación de acceso sin comercio resuelto, error de validación de
    /// entrada, intento de trial rechazado) ocurren SIN un comercio asociado, por lo que
    /// este campo admite null en esos casos en vez de un ComercioId inexistente (que violaba
    /// la FK FK_LogsAuditoria_Comercios_ComercioId con el valor centinela 0).
    /// </summary>
    public int? ComercioId { get; set; }
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
    // Nullable para concordar con ComercioId nullable (eventos de seguridad a nivel de
    // sistema sin comercio asociado y ON DELETE SET NULL).
    public Comercio? Comercio { get; set; }
    public Usuario? Usuario { get; set; }
}
