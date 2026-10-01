using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementación del servicio de chat de inteligencia de negocios.
/// Orquesta la obtención de estadísticas del comercio, construcción de prompts
/// e invocación al IAIService para responder consultas en lenguaje natural.
/// </summary>
public class ChatBusinessIntelligenceService : IChatBusinessIntelligenceService
{
    private readonly IAIService _aiService;
    private readonly AppDbContext _db;
    private readonly ILogger<ChatBusinessIntelligenceService> _logger;

    /// <summary>
    /// Marcador JSON que indica la presencia de datos de gráfico en la respuesta de IA.
    /// </summary>
    private const string ChartMarker = "```chart";
    private const string ChartMarkerEnd = "```";

    /// <summary>
    /// Roles que tienen acceso a sugerencias de inventario.
    /// </summary>
    private static readonly HashSet<string> RolesInventario = new(StringComparer.OrdinalIgnoreCase)
    {
        "Gerente", "Dueño", "Bodeguero"
    };

    /// <summary>
    /// Lista estática de sugerencias con sus categorías.
    /// </summary>
    private static readonly List<SugerenciaChat> TodasLasSugerencias =
    [
        // Categoría: ventas
        new() { Texto = "¿Cuáles fueron mis ventas totales del último mes?", Categoria = "ventas" },
        new() { Texto = "¿Cuál es mi producto más vendido?", Categoria = "ventas" },
        new() { Texto = "¿Cómo se comparan mis ventas de esta semana con la anterior?", Categoria = "ventas" },
        new() { Texto = "¿Cuál es mi ticket promedio de venta?", Categoria = "ventas" },
        new() { Texto = "¿Qué método de pago usan más mis clientes?", Categoria = "ventas" },

        // Categoría: comparacion_sucursales
        new() { Texto = "¿Cuál sucursal tiene mejor rendimiento en ventas?", Categoria = "comparacion_sucursales" },
        new() { Texto = "Compara las ventas entre mis sucursales este mes", Categoria = "comparacion_sucursales" },
        new() { Texto = "¿Qué sucursal vende más los fines de semana?", Categoria = "comparacion_sucursales" },

        // Categoría: inventario
        new() { Texto = "¿Qué productos están por debajo del stock mínimo?", Categoria = "inventario" },
        new() { Texto = "¿Cuáles son los productos con menor rotación?", Categoria = "inventario" },
        new() { Texto = "¿Cuánto inventario tengo valorizado actualmente?", Categoria = "inventario" },

        // Categoría: general
        new() { Texto = "¿Cuántos clientes nuevos tuve este mes?", Categoria = "general" },
        new() { Texto = "¿Cuál es mi horario de mayor venta?", Categoria = "general" },
    ];

    public ChatBusinessIntelligenceService(
        IAIService aiService,
        AppDbContext db,
        ILogger<ChatBusinessIntelligenceService> logger)
    {
        _aiService = aiService;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatBIResponse> ProcesarConsultaAsync(
        int comercioId, int usuarioId, string rol,
        List<ChatMessage> historial, string pregunta,
        CancellationToken ct = default)
    {
        // 1. Obtener estadísticas del comercio para inyectar como contexto
        var estadisticas = await ObtenerEstadisticasComercioAsync(comercioId, ct);

        // 2. Construir el system prompt con datos estadísticos
        var systemPrompt = ConstruirSystemPrompt(estadisticas, rol);

        // 3. Armar la lista de mensajes: system + historial + pregunta actual
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        // Agregar historial previo (mantiene contexto acumulativo - Req 7.7)
        if (historial.Count > 0)
        {
            messages.AddRange(historial);
        }

        // Agregar la pregunta actual del usuario
        messages.Add(new ChatMessage { Role = "user", Content = pregunta });

        // 4. Invocar al IAIService
        var resultado = await _aiService.ChatCompletionAsync(messages, ct);

        if (!resultado.Success)
        {
            _logger.LogWarning(
                "IAIService falló para Comercio {ComercioId}, Usuario {UsuarioId}: {Error}",
                comercioId, usuarioId, resultado.ErrorMessage);

            return new ChatBIResponse
            {
                Texto = "Lo siento, el servicio de IA no está disponible en este momento. Por favor, intenta más tarde.",
                TokensUsados = 0
            };
        }

        // 5. Parsear respuesta para extraer ChartData si existe
        var (texto, grafico) = ParsearRespuesta(resultado.Content);

        _logger.LogInformation(
            "Consulta BI procesada para Comercio {ComercioId}, Usuario {UsuarioId}. Tokens: {Tokens}. Gráfico: {TieneGrafico}",
            comercioId, usuarioId, resultado.TotalTokens, grafico is not null);

        return new ChatBIResponse
        {
            Texto = texto,
            Grafico = grafico,
            TokensUsados = resultado.TotalTokens
        };
    }

    /// <inheritdoc />
    public List<SugerenciaChat> ObtenerSugerencias(int numSucursales, string rol)
    {
        var sugerencias = new List<SugerenciaChat>();

        foreach (var sugerencia in TodasLasSugerencias)
        {
            // Filtrar comparación entre sucursales: solo si numSucursales > 1 (Req 7.3)
            if (sugerencia.Categoria == "comparacion_sucursales" && numSucursales <= 1)
                continue;

            // Filtrar inventario: solo para roles Gerente, Dueño, Bodeguero (Req 7.4)
            if (sugerencia.Categoria == "inventario" && !RolesInventario.Contains(rol))
                continue;

            sugerencias.Add(sugerencia);
        }

        return sugerencias;
    }

    /// <summary>
    /// Obtiene estadísticas resumidas del comercio para inyectar en el prompt de contexto.
    /// </summary>
    private async Task<EstadisticasComercio> ObtenerEstadisticasComercioAsync(
        int comercioId, CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var inicioMes = new DateTime(ahora.Year, ahora.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var inicioSemana = ahora.AddDays(-(int)ahora.DayOfWeek);

        // Consultar ventas del mes actual filtradas por ComercioId
        var ventasMes = await _db.Ventas
            .IgnoreQueryFilters()
            .Where(v => v.ComercioId == comercioId && v.FechaVenta >= inicioMes)
            .ToListAsync(ct);

        // Total de ventas del mes
        var totalVentasMes = ventasMes.Sum(v => v.Total);
        var cantidadVentasMes = ventasMes.Count;

        // Ventas de la semana actual
        var ventasSemana = ventasMes.Where(v => v.FechaVenta >= inicioSemana).ToList();
        var totalVentasSemana = ventasSemana.Sum(v => v.Total);

        // Ticket promedio
        var ticketPromedio = cantidadVentasMes > 0 ? totalVentasMes / cantidadVentasMes : 0;

        // Métodos de pago más usados
        var metodosPago = ventasMes
            .GroupBy(v => v.MetodoPago)
            .Select(g => new { Metodo = g.Key, Cantidad = g.Count() })
            .OrderByDescending(g => g.Cantidad)
            .Take(3)
            .ToList();

        // Productos más vendidos (top 5)
        var productosMasVendidos = await _db.DetalleVentas
            .IgnoreQueryFilters()
            .Where(d => d.Venta.ComercioId == comercioId && d.Venta.FechaVenta >= inicioMes)
            .GroupBy(d => d.Producto.Nombre)
            .Select(g => new { Producto = g.Key, Cantidad = g.Sum(d => d.Cantidad) })
            .OrderByDescending(g => g.Cantidad)
            .Take(5)
            .ToListAsync(ct);

        // Número de sucursales
        var numSucursales = await _db.Sucursales
            .IgnoreQueryFilters()
            .CountAsync(s => s.ComercioId == comercioId, ct);

        // Número de productos activos
        var numProductos = await _db.Productos
            .IgnoreQueryFilters()
            .CountAsync(p => p.ComercioId == comercioId, ct);

        return new EstadisticasComercio
        {
            TotalVentasMes = totalVentasMes,
            CantidadVentasMes = cantidadVentasMes,
            TotalVentasSemana = totalVentasSemana,
            TicketPromedio = ticketPromedio,
            MetodosPago = metodosPago.Select(m => $"{m.Metodo}: {m.Cantidad}").ToList(),
            ProductosMasVendidos = productosMasVendidos.Select(p => $"{p.Producto}: {p.Cantidad:F0} unidades").ToList(),
            NumSucursales = numSucursales,
            NumProductos = numProductos,
            FechaConsulta = ahora
        };
    }

    /// <summary>
    /// Construye el system prompt inyectando las estadísticas del comercio como contexto.
    /// </summary>
    private static string ConstruirSystemPrompt(EstadisticasComercio stats, string rol)
    {
        var chartExample = "{\"tipo\":\"bar|line|pie\",\"labels\":[\"etiqueta1\",\"etiqueta2\"],\"valores\":[100,200],\"titulo\":\"Título del gráfico\"}";

        return $"""
            Eres un asistente de inteligencia de negocios para un sistema de Punto de Venta (POS).
            Tu rol es responder preguntas sobre las estadísticas y métricas del negocio del usuario.
            El usuario tiene el rol de: {rol}.
            
            DATOS ACTUALES DEL NEGOCIO (mes en curso):
            - Ventas totales del mes: ${stats.TotalVentasMes:F2}
            - Cantidad de transacciones del mes: {stats.CantidadVentasMes}
            - Ventas de la semana actual: ${stats.TotalVentasSemana:F2}
            - Ticket promedio: ${stats.TicketPromedio:F2}
            - Métodos de pago más usados: {string.Join(", ", stats.MetodosPago)}
            - Productos más vendidos: {string.Join(", ", stats.ProductosMasVendidos)}
            - Número de sucursales: {stats.NumSucursales}
            - Número de productos activos: {stats.NumProductos}
            - Fecha de consulta: {stats.FechaConsulta:yyyy-MM-dd HH:mm} UTC
            
            INSTRUCCIONES:
            - Responde en español de forma clara y concisa.
            - Si la pregunta se puede responder con los datos proporcionados, hazlo directamente.
            - Si la respuesta incluye datos numéricos que se beneficiarían de una visualización gráfica,
              incluye un bloque JSON con el formato de gráfico al final de tu respuesta.
            - Para incluir un gráfico, usa el siguiente formato exacto:
              ```chart
              {chartExample}
              ```
            - Solo incluye gráficos cuando la información sea claramente numérica o temporal.
            - No inventes datos que no estén en el contexto proporcionado.
            - Si no tienes suficiente información para responder, indícalo amablemente.
            """;
    }

    /// <summary>
    /// Parsea la respuesta de la IA para extraer texto y datos de gráfico opcionales.
    /// Busca bloques ```chart con JSON de ChartData.
    /// </summary>
    private static (string texto, ChartData? grafico) ParsearRespuesta(string contenido)
    {
        if (string.IsNullOrWhiteSpace(contenido))
            return (string.Empty, null);

        // Buscar el marcador de gráfico
        var chartIndex = contenido.IndexOf(ChartMarker, StringComparison.OrdinalIgnoreCase);
        if (chartIndex < 0)
            return (contenido.Trim(), null);

        // Extraer el texto antes del bloque de gráfico
        var textoAntes = contenido[..chartIndex].Trim();

        // Extraer el JSON del gráfico
        var jsonStart = chartIndex + ChartMarker.Length;
        var jsonEnd = contenido.IndexOf(ChartMarkerEnd, jsonStart, StringComparison.Ordinal);

        if (jsonEnd < 0)
            return (contenido.Trim(), null);

        var jsonContent = contenido[jsonStart..jsonEnd].Trim();

        // Extraer texto después del bloque de gráfico (si existe)
        var textoDespues = string.Empty;
        var despuesStart = jsonEnd + ChartMarkerEnd.Length;
        if (despuesStart < contenido.Length)
        {
            textoDespues = contenido[despuesStart..].Trim();
        }

        // Combinar texto
        var textoFinal = string.IsNullOrEmpty(textoDespues)
            ? textoAntes
            : $"{textoAntes}\n\n{textoDespues}";

        // Intentar parsear el JSON del gráfico
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var chartData = JsonSerializer.Deserialize<ChartData>(jsonContent, options);

            if (chartData is not null &&
                !string.IsNullOrEmpty(chartData.Tipo) &&
                chartData.Labels.Count > 0 &&
                chartData.Valores.Count > 0)
            {
                return (textoFinal, chartData);
            }
        }
        catch (JsonException ex)
        {
            // Si el JSON no es válido, retornar solo el texto completo sin gráfico
            // No se propaga la excepción para no interrumpir la respuesta al usuario
            _ = ex; // Suprimir warning de variable no usada
        }

        return (textoFinal, null);
    }

    /// <summary>
    /// Clase interna para almacenar estadísticas del comercio obtenidas de la base de datos.
    /// </summary>
    private sealed class EstadisticasComercio
    {
        public decimal TotalVentasMes { get; init; }
        public int CantidadVentasMes { get; init; }
        public decimal TotalVentasSemana { get; init; }
        public decimal TicketPromedio { get; init; }
        public List<string> MetodosPago { get; init; } = [];
        public List<string> ProductosMasVendidos { get; init; } = [];
        public int NumSucursales { get; init; }
        public int NumProductos { get; init; }
        public DateTime FechaConsulta { get; init; }
    }
}
