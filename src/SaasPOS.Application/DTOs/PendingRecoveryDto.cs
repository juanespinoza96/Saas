namespace SaasPOS.Application.DTOs;

/// <summary>
/// Representa una SolicitudRecuperacion visible en la bandeja de aprobación del POS
/// (Requirements 5, 12). Incluye los datos del solicitante, el estado y la información
/// de expiración calculada con validación perezosa (minutos restantes y si la contraseña
/// temporal sigue vigente).
/// </summary>
public class PendingRecoveryDto
{
    /// <summary>Identificador de la SolicitudRecuperacion.</summary>
    public int RequestId { get; set; }

    /// <summary>Identificador del usuario solicitante.</summary>
    public int UsuarioId { get; set; }

    /// <summary>Nombre del usuario solicitante.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Correo del usuario solicitante.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Rol del usuario solicitante.</summary>
    public string Rol { get; set; } = string.Empty;

    /// <summary>Estado de la solicitud: Pendiente, Aprobada o Rechazada.</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>Marca de tiempo (UTC) en que se creó la solicitud.</summary>
    public DateTime FechaSolicitud { get; set; }

    /// <summary>Fecha de expiración de la contraseña temporal; nula mientras la solicitud está Pendiente.</summary>
    public DateTime? FechaExpiracion { get; set; }

    /// <summary>Minutos restantes antes de la expiración (0 si ya expiró o no aplica).</summary>
    public int MinutosRestantes { get; set; }

    /// <summary>Indica si la contraseña temporal aún es vigente (Aprobada y sin expirar).</summary>
    public bool TienePasswordTemporalVigente { get; set; }
}
