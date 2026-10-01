using SaasPOS.Application.DTOs;

namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Servicio de chat de inteligencia de negocios.
/// Orquesta la obtención de estadísticas del comercio e invocación al IAIService
/// para procesar consultas en lenguaje natural sobre datos del negocio.
/// </summary>
public interface IChatBusinessIntelligenceService
{
    /// <summary>
    /// Procesa una consulta de inteligencia de negocios utilizando IA.
    /// Obtiene estadísticas del comercio, construye el prompt con contexto,
    /// invoca al IAIService y parsea la respuesta para extraer datos de gráficos.
    /// </summary>
    /// <param name="comercioId">Identificador del comercio autenticado.</param>
    /// <param name="usuarioId">Identificador del usuario que realiza la consulta.</param>
    /// <param name="rol">Rol del usuario (Gerente, Dueño, Cajero, etc.).</param>
    /// <param name="historial">Historial de mensajes previos para contexto acumulativo.</param>
    /// <param name="pregunta">Pregunta en lenguaje natural del usuario.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Respuesta con texto, gráfico opcional y tokens consumidos.</returns>
    Task<ChatBIResponse> ProcesarConsultaAsync(
        int comercioId, int usuarioId, string rol,
        List<ChatMessage> historial, string pregunta,
        CancellationToken ct = default);

    /// <summary>
    /// Obtiene las sugerencias de preguntas filtradas según el contexto del comercio y usuario.
    /// Filtra sugerencias de comparación entre sucursales (solo si numSucursales > 1)
    /// y sugerencias de inventario (solo para roles Gerente, Dueño, Bodeguero).
    /// </summary>
    /// <param name="numSucursales">Número de sucursales del comercio.</param>
    /// <param name="rol">Rol del usuario actual.</param>
    /// <returns>Lista de sugerencias filtradas según el contexto.</returns>
    List<SugerenciaChat> ObtenerSugerencias(int numSucursales, string rol);
}
