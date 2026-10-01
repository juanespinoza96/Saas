namespace SaasPOS.Application.DTOs;

public class AuthResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// Código de error específico para distinguir tipos de fallo.
    /// Ejemplo: "INVALID_CREDENTIALS", "USER_INACTIVE"
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Indica si el usuario debe cambiar su contraseña de forma obligatoria (Bandera_Cambio activa).
    /// Se expone para que GenerateJwt pueda insertar el claim `must_change_password` (Requirement 8).
    /// </summary>
    public bool MustChangePassword { get; set; }

    public static AuthResult Ok(string token) => new() { Success = true, Token = token };
    public static AuthResult Fail(string error, string? code = null) => new() { Success = false, ErrorMessage = error, ErrorCode = code };
}
