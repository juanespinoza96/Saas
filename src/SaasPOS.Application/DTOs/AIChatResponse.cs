namespace SaasPOS.Application.DTOs;

/// <summary>
/// Respuesta del endpoint de chat de IA al cliente.
/// </summary>
public class AIChatResponse
{
    /// <summary>
    /// Contenido generado por el modelo de IA.
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
    /// Modelo de IA utilizado para la generación.
    /// </summary>
    public string Model { get; set; } = string.Empty;
}
