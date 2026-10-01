using System.ComponentModel.DataAnnotations;

namespace SaasPOS.Application.DTOs;

/// <summary>
/// Cuerpo de la petición para rechazar una SolicitudRecuperacion (Requirement 15).
/// El motivo de rechazo es obligatorio; el backend vuelve a validar que no esté vacío.
/// </summary>
public class RejectRecoveryRequest
{
    /// <summary>Motivo por el cual el aprobador rechaza la solicitud (obligatorio).</summary>
    [Required]
    public string MotivoRechazo { get; set; } = string.Empty;
}
