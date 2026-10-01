namespace SaasPOS.Application.DTOs;

/// <summary>
/// Solicitud al endpoint de chat de IA.
/// </summary>
public class AIChatRequest
{
    /// <summary>
    /// Lista de mensajes de la conversación. Mínimo 1, máximo 50 elementos.
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>
    /// Temperatura para la generación. Rango: 0.0 a 2.0. Opcional.
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Máximo de tokens a generar. Rango: 1 a 16384. Opcional.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Top-P (nucleus sampling). Rango: 0.0 a 1.0. Opcional.
    /// </summary>
    public double? TopP { get; set; }
}
