namespace SaasPOS.Application.DTOs;

/// <summary>
/// Respuesta de la aprobación de una SolicitudRecuperacion y de la reconsulta de la
/// contraseña temporal (Requirements 4, 5). Contiene la contraseña temporal en texto plano
/// y su fecha de expiración para que el aprobador la comunique al solicitante.
/// </summary>
public class ApproveResponse
{
    /// <summary>Contraseña temporal en texto plano (12 caracteres, cumple la Politica_Password).</summary>
    public string TempPassword { get; set; } = string.Empty;

    /// <summary>Fecha de expiración (UTC) de la contraseña temporal.</summary>
    public DateTime FechaExpiracion { get; set; }
}
