namespace SaasPOS.Infrastructure.Configuration;

/// <summary>
/// Configuración para la integración con la API de NVIDIA AI.
/// Se carga desde la sección "NvidiaAI" de appsettings.json.
/// IMPORTANTE: La ApiKey debe configurarse mediante variables de entorno,
/// nunca hardcodearse en código fuente ni en appsettings versionados.
/// Variable de entorno: NvidiaAI__ApiKey
/// </summary>
public class NvidiaAISettings
{
    public const string SectionName = "NvidiaAI";

    /// <summary>
    /// URL base del endpoint de NVIDIA AI.
    /// </summary>
    public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1";

    /// <summary>
    /// Clave de autenticación para la API de NVIDIA. No debe exponerse en logs ni respuestas.
    /// Configurar mediante variable de entorno: NvidiaAI__ApiKey
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Modelo de IA a utilizar.
    /// </summary>
    public string Model { get; set; } = "z-ai/glm-5.2";

    /// <summary>
    /// Temperatura para la generación de texto. Rango válido: 0.0 a 2.0.
    /// </summary>
    public double Temperature { get; set; } = 1.0;

    /// <summary>
    /// Top-P (nucleus sampling). Rango válido: 0.0 a 1.0.
    /// </summary>
    public double TopP { get; set; } = 1.0;

    /// <summary>
    /// Máximo de tokens a generar por respuesta. Rango válido: 1 a 131072.
    /// </summary>
    public int MaxTokens { get; set; } = 16384;

    /// <summary>
    /// Seed para reproducibilidad de respuestas. Null = aleatorio.
    /// </summary>
    public int? Seed { get; set; } = 42;

    /// <summary>
    /// Habilitar modo de razonamiento (thinking) del modelo.
    /// </summary>
    public bool EnableThinking { get; set; } = true;

    /// <summary>
    /// Si es true, el contenido de razonamiento se omite de la respuesta final.
    /// Si es false, se incluye el razonamiento en la respuesta.
    /// </summary>
    public bool ClearThinking { get; set; } = false;

    /// <summary>
    /// Timeout en segundos para las solicitudes HTTP. Rango válido: 1 a 300.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Límite de solicitudes por minuto por tenant para rate limiting.
    /// </summary>
    public int RequestsPerMinutePerTenant { get; set; } = 10;
}
