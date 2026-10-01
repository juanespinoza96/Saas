namespace SaasPOS.Application.DTOs;

/// <summary>
/// Respuesta del endpoint de health check de IA.
/// </summary>
public class AIHealthResponse
{
    /// <summary>
    /// Estado del servicio: "healthy", "unhealthy", "unconfigured".
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Modelo configurado (null si no está configurado).
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Mensaje descriptivo (null si healthy).
    /// </summary>
    public string? Message { get; set; }
}
