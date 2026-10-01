using SaasPOS.Application.DTOs;

namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Interfaz del servicio de inteligencia artificial.
/// Permite enviar solicitudes de chat completion y verificar disponibilidad del servicio.
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Envía una solicitud de chat completion con parámetros por defecto de configuración.
    /// </summary>
    /// <param name="messages">Lista de mensajes de la conversación (mínimo 1 elemento).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Resultado de la completion con indicador de éxito, contenido y tokens utilizados.</returns>
    Task<AICompletionResult> ChatCompletionAsync(
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía una solicitud de chat completion con parámetros opcionales que sobreescriben la configuración.
    /// </summary>
    /// <param name="messages">Lista de mensajes de la conversación (mínimo 1 elemento).</param>
    /// <param name="temperature">Temperatura para la generación. Rango válido: 0.0 a 2.0.</param>
    /// <param name="maxTokens">Máximo de tokens a generar. Rango válido: 1 a 16384.</param>
    /// <param name="topP">Top-P (nucleus sampling). Rango válido: 0.0 a 1.0.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Resultado de la completion con indicador de éxito, contenido y tokens utilizados.</returns>
    Task<AICompletionResult> ChatCompletionAsync(
        List<ChatMessage> messages,
        double? temperature = null,
        int? maxTokens = null,
        double? topP = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica si el servicio de IA está configurado y el endpoint es alcanzable.
    /// Timeout máximo de verificación: 5 segundos.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>True si el servicio está disponible, false en caso contrario.</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
