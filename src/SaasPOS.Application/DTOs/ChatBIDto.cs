namespace SaasPOS.Application.DTOs;

/// <summary>
/// Request para enviar una consulta al chat de inteligencia de negocios.
/// </summary>
public class ChatBIRequest
{
    /// <summary>
    /// Pregunta en lenguaje natural sobre estadísticas del negocio.
    /// </summary>
    public string Pregunta { get; set; } = string.Empty;

    /// <summary>
    /// Historial de mensajes de la conversación para mantener contexto acumulativo.
    /// Utiliza el formato de mensajes existente con roles system/user/assistant.
    /// </summary>
    public List<ChatMessage> Historial { get; set; } = [];
}

/// <summary>
/// Response del chat de inteligencia de negocios.
/// </summary>
public class ChatBIResponse
{
    /// <summary>
    /// Texto de respuesta generado por la IA.
    /// </summary>
    public string Texto { get; set; } = string.Empty;

    /// <summary>
    /// Datos para renderizar un gráfico inline (opcional).
    /// Presente cuando la respuesta incluye datos numéricos o series temporales.
    /// </summary>
    public ChartData? Grafico { get; set; }

    /// <summary>
    /// Cantidad de tokens consumidos en la consulta.
    /// </summary>
    public int TokensUsados { get; set; }
}

/// <summary>
/// Datos para renderizar una visualización gráfica en el chat.
/// </summary>
public class ChartData
{
    /// <summary>
    /// Tipo de gráfico: "bar" (barras), "line" (líneas) o "pie" (pastel).
    /// </summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>
    /// Etiquetas del eje X o categorías del gráfico.
    /// </summary>
    public List<string> Labels { get; set; } = [];

    /// <summary>
    /// Valores numéricos correspondientes a cada etiqueta.
    /// </summary>
    public List<decimal> Valores { get; set; } = [];

    /// <summary>
    /// Título descriptivo del gráfico (opcional).
    /// </summary>
    public string? Titulo { get; set; }
}

/// <summary>
/// Sugerencia de pregunta para el chat de inteligencia de negocios.
/// Se filtra según el plan del comercio y el rol del usuario.
/// </summary>
public class SugerenciaChat
{
    /// <summary>
    /// Texto de la pregunta sugerida.
    /// </summary>
    public string Texto { get; set; } = string.Empty;

    /// <summary>
    /// Categoría de la sugerencia (ej: "ventas", "inventario", "comparacion_sucursales").
    /// </summary>
    public string Categoria { get; set; } = string.Empty;
}
