using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Tenants;

/// <summary>
/// Controlador de inteligencia artificial.
/// Proporciona endpoints para interactuar con el servicio de IA (chat completion).
/// Aplica rate limiting por tenant y feature gating por plan de suscripción.
/// </summary>
[ApiController]
[Route("api/tenants/ai")]
[Authorize(Roles = "Dueño,Gerente")]
[EnableRateLimiting("AIRateLimit")]
public class AIController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly ISubscriptionGuard _subscriptionGuard;
    private readonly ITenantContext _tenantContext;
    private readonly NvidiaAISettings _aiSettings;
    private readonly IAuditService _auditService;
    private readonly ILogger<AIController> _logger;
    private readonly IChatBusinessIntelligenceService _chatBIService;
    private readonly AppDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Roles válidos para los mensajes de chat.
    /// </summary>
    private static readonly HashSet<string> ValidRoles = new(StringComparer.Ordinal)
    {
        "system", "user", "assistant"
    };

    public AIController(
        IAIService aiService,
        ISubscriptionGuard subscriptionGuard,
        ITenantContext tenantContext,
        IOptions<NvidiaAISettings> aiSettings,
        IAuditService auditService,
        ILogger<AIController> logger,
        IChatBusinessIntelligenceService chatBIService,
        AppDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _aiService = aiService;
        _subscriptionGuard = subscriptionGuard;
        _tenantContext = tenantContext;
        _aiSettings = aiSettings.Value;
        _auditService = auditService;
        _logger = logger;
        _chatBIService = chatBIService;
        _dbContext = dbContext;
        _environment = environment;
    }

    /// <summary>
    /// POST /api/tenants/ai/chat
    /// Envía una solicitud de chat completion al servicio de IA.
    /// Requiere rol Dueño o Gerente y plan con acceso a IA.
    /// </summary>
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AIChatRequest request, CancellationToken cancellationToken)
    {
        // 1. Validación de entrada
        var validationError = ValidateRequest(request);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        // 2. Obtener ComercioId del contexto del tenant (extraído del JWT vía ITenantContext)
        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "No se pudo determinar el comercio del usuario." });

        // 3. Obtener UserId e incluir ambos en logging estructurado para trazabilidad multi-tenant
        var usuarioId = GetUsuarioId();
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ComercioId"] = comercioId.Value,
            ["UserId"] = usuarioId
        }))
        {
            _logger.LogInformation("Procesando solicitud de IA para ComercioId={ComercioId}, UserId={UserId}", comercioId.Value, usuarioId);

            // 4. Verificar acceso por plan de suscripción
            var canUseAI = await _subscriptionGuard.CanUseAIAsync(comercioId.Value);
            if (!canUseAI)
                return StatusCode(403, new { error = "La funcionalidad de IA no está incluida en el plan actual del comercio." });

            // 5. Verificar disponibilidad del servicio de IA
            var isAvailable = await _aiService.IsAvailableAsync(cancellationToken);
            if (!isAvailable)
                return StatusCode(503, new { error = "El servicio de inteligencia artificial no está disponible." });

            // 6. Invocar el servicio de IA (procesamiento stateless, sin caché compartida)
            var result = await _aiService.ChatCompletionAsync(
                request.Messages,
                request.Temperature,
                request.MaxTokens,
                request.TopP,
                cancellationToken);

            // 7. Retornar respuesta según resultado
            if (result.Success)
            {
                var response = new AIChatResponse
                {
                    Content = result.Content,
                    PromptTokens = result.PromptTokens,
                    CompletionTokens = result.CompletionTokens,
                    TotalTokens = result.TotalTokens,
                    Model = _aiSettings.Model
                };

                // Registrar auditoría para solicitud exitosa (nunca almacenar contenido de mensajes)
                try
                {
                    await _auditService.RegistrarAsync(
                        comercioId.Value,
                        usuarioId,
                        "AI_ChatCompletion",
                        "AI",
                        Guid.NewGuid().ToString(),
                        null,
                        new
                        {
                            prompt_tokens = result.PromptTokens,
                            completion_tokens = result.CompletionTokens,
                            total_tokens = result.TotalTokens,
                            model = _aiSettings.Model
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fallo al registrar auditoría de solicitud AI exitosa");
                }

                _logger.LogInformation("Solicitud de IA completada exitosamente. TotalTokens={TotalTokens}", result.TotalTokens);
                return Ok(response);
            }

            // Si el servicio de IA falló, registrar auditoría de error antes de retornar
            try
            {
                var errorType = MapErrorType(result.ErrorMessage);
                await _auditService.RegistrarAsync(
                    comercioId.Value,
                    usuarioId,
                    "AI_ChatCompletion_Error",
                    "AI",
                    Guid.NewGuid().ToString(),
                    null,
                    new { error_type = errorType });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo al registrar auditoría de error AI");
            }

            _logger.LogWarning("Solicitud de IA falló. Error={ErrorMessage}", result.ErrorMessage);
            return StatusCode(502, new { error = result.ErrorMessage });
        }
    }

    /// <summary>
    /// GET /api/tenants/ai/health
    /// Verifica el estado de la conexión con el servicio de IA.
    /// Requiere rol Dueño exclusivamente.
    /// </summary>
    [HttpGet("health")]
    [Authorize(Roles = "Dueño")]
    public async Task<IActionResult> Health()
    {
        // Req 3.8: En producción, deshabilitar endpoints de diagnóstico detallado.
        // Retornar solo el status simple sin exponer información del entorno.
        if (!_environment.IsDevelopment())
        {
            return Ok(new { status = "healthy" });
        }

        // 1. Obtener ComercioId del contexto del tenant (extraído del JWT vía ITenantContext)
        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "No se pudo determinar el comercio del usuario." });

        // 2. Incluir ComercioId y UserId en logging estructurado
        var usuarioId = GetUsuarioId();
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ComercioId"] = comercioId.Value,
            ["UserId"] = usuarioId
        }))
        {
            _logger.LogInformation("Verificando salud del servicio de IA");

            // 3. Verificar acceso por plan de suscripción
            var canUseAI = await _subscriptionGuard.CanUseAIAsync(comercioId.Value);
            if (!canUseAI)
                return StatusCode(403, new { error = "La funcionalidad de IA no está disponible para el plan actual." });

            // 4. Verificar si la API key está configurada
            if (string.IsNullOrEmpty(_aiSettings.ApiKey))
            {
                _logger.LogInformation("Health check: servicio no configurado");
                return Ok(new AIHealthResponse
                {
                    Status = "unconfigured",
                    Model = null,
                    Message = "La clave de API no está configurada."
                });
            }

            // 5. Verificar disponibilidad del servicio con timeout de 10 segundos
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var isAvailable = await _aiService.IsAvailableAsync(cts.Token);

                if (isAvailable)
                {
                    _logger.LogInformation("Health check: servicio saludable");
                    return Ok(new AIHealthResponse
                    {
                        Status = "healthy",
                        Model = _aiSettings.Model,
                        Message = null
                    });
                }

                _logger.LogWarning("Health check: servicio no saludable");
                return Ok(new AIHealthResponse
                {
                    Status = "unhealthy",
                    Model = _aiSettings.Model,
                    Message = "El servicio de IA no está respondiendo correctamente."
                });
            }
            catch (OperationCanceledException)
            {
                // Timeout de 10 segundos alcanzado
                _logger.LogWarning("Health check: timeout alcanzado");
                return Ok(new AIHealthResponse
                {
                    Status = "unhealthy",
                    Model = _aiSettings.Model,
                    Message = "timeout"
                });
            }
            catch (Exception)
            {
                // Cualquier otra excepción durante la verificación
                _logger.LogWarning("Health check: error de conectividad");
                return Ok(new AIHealthResponse
                {
                    Status = "unhealthy",
                    Model = _aiSettings.Model,
                    Message = "Error al verificar la conectividad del servicio de IA."
                });
            }
        }
    }

    /// <summary>
    /// POST /api/tenants/ai/chat-bi
    /// Procesa una consulta de inteligencia de negocios en lenguaje natural.
    /// Requiere rol Dueño o Gerente y plan con acceso a IA (Plan Empresarial).
    /// Rate limit: 20 consultas/hora por usuario.
    /// </summary>
    [HttpPost("chat-bi")]
    [EnableRateLimiting("ChatBIRateLimit")]
    public async Task<IActionResult> ChatBI([FromBody] ChatBIRequest request, CancellationToken cancellationToken)
    {
        // 1. Validación de entrada: pregunta no vacía y no excesivamente larga
        if (string.IsNullOrWhiteSpace(request.Pregunta))
            return BadRequest(new { error = "La pregunta no puede estar vacía." });

        if (request.Pregunta.Length > 2000)
            return BadRequest(new { error = "La pregunta no puede exceder 2,000 caracteres." });

        // 2. Validar historial de mensajes (máximo 50, roles válidos)
        if (request.Historial is not null && request.Historial.Count > 50)
            return BadRequest(new { error = "El historial no puede exceder 50 mensajes." });

        if (request.Historial is not null)
        {
            foreach (var msg in request.Historial)
            {
                if (!ValidRoles.Contains(msg.Role))
                    return BadRequest(new { error = $"Rol inválido '{msg.Role}' en el historial. Los roles válidos son: system, user, assistant." });

                if (string.IsNullOrWhiteSpace(msg.Content))
                    return BadRequest(new { error = "El contenido del mensaje en el historial no puede estar vacío." });
            }
        }

        // 3. Obtener ComercioId del contexto del tenant
        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "No se pudo determinar el comercio del usuario." });

        // 4. Obtener UserId y rol del usuario autenticado
        var usuarioId = GetUsuarioId();
        var rol = User.FindFirstValue("role") ?? "Gerente";

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ComercioId"] = comercioId.Value,
            ["UserId"] = usuarioId
        }))
        {
            _logger.LogInformation("Procesando consulta Chat BI para ComercioId={ComercioId}, UserId={UserId}", comercioId.Value, usuarioId);

            // 5. Verificar acceso por plan de suscripción (solo Plan Empresarial)
            var canUseAI = await _subscriptionGuard.CanUseAIAsync(comercioId.Value);
            if (!canUseAI)
                return StatusCode(403, new { error = "La funcionalidad de IA no está incluida en el plan actual del comercio." });

            // 6. Verificar disponibilidad del servicio de IA
            var isAvailable = await _aiService.IsAvailableAsync(cancellationToken);
            if (!isAvailable)
                return StatusCode(503, new { error = "El servicio de inteligencia artificial no está disponible temporalmente. Intente más tarde." });

            // 7. Invocar el servicio de Chat BI
            try
            {
                var resultado = await _chatBIService.ProcesarConsultaAsync(
                    comercioId.Value,
                    usuarioId,
                    rol,
                    request.Historial ?? [],
                    request.Pregunta,
                    cancellationToken);

                // 8. Verificar que la respuesta sea válida
                if (string.IsNullOrWhiteSpace(resultado.Texto))
                {
                    _logger.LogWarning("Chat BI retornó respuesta vacía para ComercioId={ComercioId}", comercioId.Value);
                    return StatusCode(503, new { error = "El servicio de inteligencia artificial no está disponible temporalmente. Intente más tarde." });
                }

                _logger.LogInformation("Chat BI completado exitosamente. TokensUsados={TokensUsados}", resultado.TokensUsados);
                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar consulta Chat BI para ComercioId={ComercioId}", comercioId.Value);
                return StatusCode(503, new { error = "El servicio de inteligencia artificial no está disponible temporalmente. Intente más tarde." });
            }
        }
    }

    /// <summary>
    /// GET /api/tenants/ai/chat-bi/sugerencias
    /// Obtiene la lista de preguntas sugeridas filtradas según el plan del comercio,
    /// el número de sucursales y el rol del usuario.
    /// </summary>
    [HttpGet("chat-bi/sugerencias")]
    public async Task<IActionResult> ObtenerSugerencias(CancellationToken cancellationToken)
    {
        // 1. Obtener ComercioId del contexto del tenant
        var comercioId = _tenantContext.ComercioId;
        if (comercioId is null)
            return Unauthorized(new { error = "No se pudo determinar el comercio del usuario." });

        var usuarioId = GetUsuarioId();
        var rol = User.FindFirstValue("role") ?? "Gerente";

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ComercioId"] = comercioId.Value,
            ["UserId"] = usuarioId
        }))
        {
            _logger.LogInformation("Obteniendo sugerencias Chat BI para ComercioId={ComercioId}, Rol={Rol}", comercioId.Value, rol);

            // 2. Verificar acceso por plan de suscripción
            var canUseAI = await _subscriptionGuard.CanUseAIAsync(comercioId.Value);
            if (!canUseAI)
                return StatusCode(403, new { error = "La funcionalidad de IA no está incluida en el plan actual del comercio." });

            // 3. Obtener el número de sucursales del comercio
            var numSucursales = await _dbContext.Sucursales
                .CountAsync(s => s.ComercioId == comercioId.Value, cancellationToken);

            // 4. Obtener sugerencias filtradas según contexto
            var sugerencias = _chatBIService.ObtenerSugerencias(numSucursales, rol);

            _logger.LogInformation("Retornando {Count} sugerencias para ComercioId={ComercioId}", sugerencias.Count, comercioId.Value);
            return Ok(sugerencias);
        }
    }

    /// <summary>
    /// Valida manualmente el AIChatRequest según las reglas de negocio.
    /// Retorna null si la solicitud es válida, o el mensaje de error si no lo es.
    /// </summary>
    private static string? ValidateRequest(AIChatRequest request)
    {
        // Validar que la lista de mensajes no esté vacía o nula
        if (request.Messages is null || request.Messages.Count == 0)
            return "Se requiere al menos un mensaje.";

        // Validar máximo de mensajes
        if (request.Messages.Count > 50)
            return "Se permiten máximo 50 mensajes por solicitud.";

        // Validar cada mensaje
        foreach (var message in request.Messages)
        {
            // Validar rol
            if (!ValidRoles.Contains(message.Role))
                return $"Rol inválido '{message.Role}'. Los roles válidos son: system, user, assistant.";

            // Validar contenido no vacío
            if (string.IsNullOrEmpty(message.Content))
                return "El contenido del mensaje no puede estar vacío.";

            // Validar longitud de contenido
            if (message.Content.Length > 32000)
                return "El contenido del mensaje no puede exceder 32,000 caracteres.";
        }

        // Validar rangos numéricos opcionales
        if (request.Temperature.HasValue && (request.Temperature.Value < 0.0 || request.Temperature.Value > 2.0))
            return "Temperature debe estar entre 0.0 y 2.0.";

        if (request.MaxTokens.HasValue && (request.MaxTokens.Value < 1 || request.MaxTokens.Value > 16384))
            return "MaxTokens debe estar entre 1 y 16384.";

        if (request.TopP.HasValue && (request.TopP.Value < 0.0 || request.TopP.Value > 1.0))
            return "TopP debe estar entre 0.0 y 1.0.";

        return null;
    }

    /// <summary>
    /// Obtiene el ID del usuario autenticado desde los claims JWT.
    /// </summary>
    private int GetUsuarioId() =>
        int.Parse(User.FindFirstValue("sub")!);

    /// <summary>
    /// Mapea el mensaje de error del servicio de IA a un tipo de error para auditoría.
    /// Nunca almacena contenido de mensajes del usuario ni respuesta de IA.
    /// </summary>
    private static string MapErrorType(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return "network_error";

        var lower = errorMessage.ToLowerInvariant();

        if (lower.Contains("tiempo") || lower.Contains("timeout"))
            return "timeout";

        if (lower.Contains("límite de solicitudes") || lower.Contains("rate"))
            return "rate_limit";

        if (lower.Contains("autenticación") || lower.Contains("auth"))
            return "auth_error";

        if (lower.Contains("error interno del proveedor") || lower.Contains("5xx") || lower.Contains("server"))
            return "server_error";

        return "network_error";
    }
}
