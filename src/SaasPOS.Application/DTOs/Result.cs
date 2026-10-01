namespace SaasPOS.Application.DTOs;

/// <summary>
/// Resultado genérico de una operación de servicio.
/// Permite retornar éxito o fallo con código de error y mensaje descriptivo.
/// </summary>
public class Result
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }

    public static Result Ok() => new() { Success = true };
    public static Result Fail(string error, string? code = null) => new() { Success = false, ErrorMessage = error, ErrorCode = code };
}
