namespace SaasPOS.Application.DTOs;

/// <summary>
/// Resultado interno de una invocación al servicio de IA.
/// </summary>
public class AICompletionResult
{
    /// <summary>
    /// Indica si la solicitud fue exitosa.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Contenido generado por el modelo (vacío si hay error).
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Cantidad de tokens utilizados en el prompt.
    /// </summary>
    public int PromptTokens { get; set; }

    /// <summary>
    /// Cantidad de tokens generados en la respuesta.
    /// </summary>
    public int CompletionTokens { get; set; }

    /// <summary>
    /// Total de tokens consumidos (prompt + completion).
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// Mensaje de error cuando la solicitud falla (null si exitosa).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Crea un resultado exitoso con el contenido y métricas de tokens.
    /// </summary>
    public static AICompletionResult Ok(string content, int promptTokens, int completionTokens, int totalTokens)
        => new() { Success = true, Content = content, PromptTokens = promptTokens, CompletionTokens = completionTokens, TotalTokens = totalTokens };

    /// <summary>
    /// Crea un resultado fallido con el mensaje de error.
    /// </summary>
    public static AICompletionResult Fail(string errorMessage)
        => new() { Success = false, ErrorMessage = errorMessage };
}
