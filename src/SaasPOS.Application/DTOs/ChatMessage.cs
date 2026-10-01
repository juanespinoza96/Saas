namespace SaasPOS.Application.DTOs;

/// <summary>
/// Mensaje individual en una conversación de chat con el servicio de IA.
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// Rol del mensaje. Valores permitidos: "system", "user", "assistant".
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Contenido del mensaje. Máximo 32,000 caracteres.
    /// </summary>
    public string Content { get; set; } = string.Empty;
}
