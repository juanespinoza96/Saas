using System.ComponentModel.DataAnnotations;

namespace SaasPOS.Application.DTOs;

/// <summary>
/// Cuerpo de la petición para el cambio de contraseña obligatorio (Requirement 10).
/// El identificador del usuario se toma del claim del JWT, no del cuerpo.
/// La contraseña se valida contra la Politica_Password estricta en el backend.
/// </summary>
public class ChangePasswordRequest
{
    /// <summary>Nueva contraseña definitiva que el usuario desea establecer.</summary>
    [Required]
    public string NewPassword { get; set; } = string.Empty;
}
