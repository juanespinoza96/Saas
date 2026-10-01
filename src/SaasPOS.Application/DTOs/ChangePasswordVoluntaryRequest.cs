using System.ComponentModel.DataAnnotations;

namespace SaasPOS.Application.DTOs;

/// <summary>
/// Cuerpo de la petición para el cambio de contraseña voluntario desde Configuración
/// (Requirement 17). Exige la contraseña actual (verificada con BCrypt) y la nueva contraseña
/// (validada contra la Politica_Password estricta).
/// </summary>
public class ChangePasswordVoluntaryRequest
{
    /// <summary>Contraseña actual del usuario, verificada contra el PasswordHash.</summary>
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>Nueva contraseña que el usuario desea establecer.</summary>
    [Required]
    public string NewPassword { get; set; } = string.Empty;
}
