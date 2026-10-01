using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Configuration;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementación del servicio de IA que se comunica con la API de NVIDIA.
/// Usa IHttpClientFactory con cliente nombrado "NvidiaAI".
/// Incluye retry con backoff exponencial para errores 5xx y manejo detallado de errores HTTP.
/// </summary>
public class NvidiaAIService : IAIService
{
    private const string HttpClientName = "NvidiaAI";

    // Rangos válidos para validación de configuración
    private const int MinTimeoutSeconds = 5;
    private const int MaxTimeoutSeconds = 120;
    private const int DefaultTimeoutSeconds = 30;

    private const double MinTemperature = 0.0;
    private const double MaxTemperature = 2.0;
    private const double DefaultTemperature = 1.0;

    private const double MinTopP = 0.0;
    private const double MaxTopP = 1.0;
    private const double DefaultTopP = 0.95;

    private const int MinMaxTokens = 1;
    private const int MaxMaxTokens = 131072;
    private const int DefaultMaxTokens = 16384;

    // Configuración de retry para errores 5xx
    private const int MaxRetryAttempts = 3;
    private static readonly int[] RetryDelaysSeconds = { 2, 4, 8 };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NvidiaAISettings _settings;
    private readonly ILogger<NvidiaAIService> _logger;

    // Valores efectivos después de validar rangos
    private readonly int _effectiveTimeout;
    private readonly double _effectiveTemperature;
    private readonly double _effectiveTopP;
    private readonly int _effectiveMaxTokens;

    // Opciones de serialización JSON
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public NvidiaAIService(
        IHttpClientFactory httpClientFactory,
        IOptions<NvidiaAISettings> settings,
        ILogger<NvidiaAIService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;

        // Validar rangos de configuración y aplicar defaults si están fuera de rango
        _effectiveTimeout = ValidateRange(
            _settings.TimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds, DefaultTimeoutSeconds, nameof(_settings.TimeoutSeconds));
        _effectiveTemperature = ValidateRange(
            _settings.Temperature, MinTemperature, MaxTemperature, DefaultTemperature, nameof(_settings.Temperature));
        _effectiveTopP = ValidateRange(
            _settings.TopP, MinTopP, MaxTopP, DefaultTopP, nameof(_settings.TopP));
        _effectiveMaxTokens = ValidateRange(
            _settings.MaxTokens, MinMaxTokens, MaxMaxTokens, DefaultMaxTokens, nameof(_settings.MaxTokens));
    }

    /// <inheritdoc />
    public Task<AICompletionResult> ChatCompletionAsync(
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        return ChatCompletionAsync(messages, null, null, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AICompletionResult> ChatCompletionAsync(
        List<ChatMessage> messages,
        double? temperature = null,
        int? maxTokens = null,
        double? topP = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determinar valores efectivos: parámetros opcionales sobreescriben configuración
            var effectiveTemperature = temperature ?? _effectiveTemperature;
            var effectiveMaxTokens = maxTokens ?? _effectiveMaxTokens;
            var effectiveTopP = topP ?? _effectiveTopP;

            // Construir el body de la solicitud según formato OpenAI-compatible
            var requestBody = BuildRequestBody(messages, effectiveTemperature, effectiveMaxTokens, effectiveTopP);
            var jsonContent = JsonSerializer.Serialize(requestBody, JsonOptions);

            // Ejecutar la solicitud con retry para errores 5xx
            return await SendWithRetryAsync(jsonContent, cancellationToken);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
        {
            // Timeout de la solicitud HTTP
            _logger.LogWarning("Solicitud a NVIDIA AI excedió el tiempo de espera ({Timeout}s)", _effectiveTimeout);
            return AICompletionResult.Fail("La solicitud excedió el tiempo de espera.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelación explícita por el usuario
            _logger.LogInformation("Solicitud a NVIDIA AI fue cancelada por el usuario");
            return AICompletionResult.Fail("La solicitud fue cancelada.");
        }
        catch (HttpRequestException ex)
        {
            // Error de red/DNS/TLS - no exponer detalles internos
            _logger.LogError(ex, "Error de red al comunicarse con el proveedor de IA: {Message}", ex.Message);
            return AICompletionResult.Fail("Error de comunicación con el proveedor de IA.");
        }
        catch (SocketException ex)
        {
            // Error de socket - no exponer detalles internos
            _logger.LogError(ex, "Error de socket al comunicarse con el proveedor de IA: {Message}", ex.Message);
            return AICompletionResult.Fail("Error de comunicación con el proveedor de IA.");
        }
        catch (Exception ex)
        {
            // Error genérico de comunicación - no exponer detalles internos
            _logger.LogError(ex, "Error inesperado al comunicarse con el proveedor de IA");
            return AICompletionResult.Fail("Error de comunicación con el proveedor de IA.");
        }
    }

    /// <summary>
    /// Envía la solicitud HTTP con retry automático para errores 5xx.
    /// Implementa backoff exponencial (2s, 4s, 8s) con hasta 3 reintentos.
    /// Para errores 4xx, retorna inmediatamente sin reintentar.
    /// </summary>
    private async Task<AICompletionResult> SendWithRetryAsync(string jsonContent, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_effectiveTimeout);

        for (var attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            // Crear nueva instancia de HttpRequestMessage en cada intento (no se puede reutilizar)
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl}/chat/completions")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            var response = await client.SendAsync(request, cancellationToken);

            // Respuesta exitosa: deserializar y retornar
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return DeserializeResponse(responseContent);
            }

            var statusCode = (int)response.StatusCode;

            // HTTP 5xx: reintentar con backoff exponencial si quedan intentos
            if (statusCode >= 500)
            {
                _logger.LogWarning(
                    "Solicitud a NVIDIA AI falló con código {StatusCode} (intento {Attempt}/{MaxRetries})",
                    statusCode, attempt + 1, MaxRetryAttempts + 1);

                if (attempt < MaxRetryAttempts)
                {
                    var delaySeconds = RetryDelaysSeconds[attempt];
                    _logger.LogInformation("Reintentando en {Delay}s...", delaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    continue;
                }

                // Agotados todos los reintentos
                _logger.LogError(
                    "Solicitud a NVIDIA AI falló después de {MaxRetries} reintentos con código {StatusCode}",
                    MaxRetryAttempts, statusCode);
                return AICompletionResult.Fail("Error interno del proveedor de IA.");
            }

            // HTTP 429: Too Many Requests - no reintentar
            if (statusCode == 429)
            {
                var retryAfterMessage = "Se excedió el límite de solicitudes del proveedor de IA.";

                // Incluir valor de Retry-After si está presente
                if (response.Headers.RetryAfter?.Delta != null)
                {
                    var retryAfterSeconds = (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                    retryAfterMessage = $"Se excedió el límite de solicitudes del proveedor de IA. Reintentar después de {retryAfterSeconds} segundos.";
                }
                else if (response.Headers.RetryAfter?.Date != null)
                {
                    var retryAfterDate = response.Headers.RetryAfter.Date.Value;
                    var waitSeconds = (int)Math.Max(0, (retryAfterDate - DateTimeOffset.UtcNow).TotalSeconds);
                    retryAfterMessage = $"Se excedió el límite de solicitudes del proveedor de IA. Reintentar después de {waitSeconds} segundos.";
                }

                _logger.LogWarning("Solicitud a NVIDIA AI rechazada por límite de tasa (429)");
                return AICompletionResult.Fail(retryAfterMessage);
            }

            // HTTP 401/403: Error de autenticación - no exponer API key
            if (statusCode == 401 || statusCode == 403)
            {
                _logger.LogWarning(
                    "Error de autenticación con NVIDIA AI. Código: {StatusCode}. Verificar configuración de credenciales.",
                    statusCode);
                return AICompletionResult.Fail("Error de autenticación con el proveedor de IA.");
            }

            // HTTP 4xx genérico (400, 404, etc.) - incluir código HTTP en el mensaje
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Solicitud a NVIDIA AI falló con código {StatusCode}. Cuerpo de respuesta: {ResponseBody}",
                    statusCode, responseBody);
                return AICompletionResult.Fail($"Error de solicitud al proveedor de IA. Código HTTP: {statusCode}");
            }
        }

        // Este punto no debería alcanzarse, pero por seguridad
        return AICompletionResult.Fail("Error interno del proveedor de IA.");
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Si no hay API key configurada, el servicio no está disponible
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning("El servicio de IA no está configurado: API key vacía");
            return false;
        }

        try
        {
            // Intentar una llamada ligera con timeout de 5 segundos
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var client = _httpClientFactory.CreateClient(HttpClientName);
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.BaseUrl}/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            var response = await client.SendAsync(request, timeoutCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Verificación de disponibilidad del servicio de IA falló");
            return false;
        }
    }

    /// <summary>
    /// Construye el objeto del body de solicitud para la API de NVIDIA.
    /// Estructura: model, messages, temperature, top_p, max_tokens, seed, stream, chat_template_kwargs.
    /// Los parámetros chat_template_kwargs controlan el modo de razonamiento del modelo.
    /// </summary>
    private object BuildRequestBody(List<ChatMessage> messages, double temperature, int maxTokens, double topP)
    {
        return new
        {
            model = _settings.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature,
            top_p = topP,
            max_tokens = maxTokens,
            seed = _settings.Seed,
            stream = false,
            chat_template_kwargs = new
            {
                enable_thinking = _settings.EnableThinking,
                clear_thinking = _settings.ClearThinking
            }
        };
    }

    /// <summary>
    /// Deserializa la respuesta de la API de NVIDIA en formato OpenAI-compatible.
    /// Extrae choices[0].message.content y usage (prompt_tokens, completion_tokens, total_tokens).
    /// Si el modelo incluye reasoning_content (modo thinking), lo concatena al contenido principal.
    /// </summary>
    private AICompletionResult DeserializeResponse(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;

            // Extraer el contenido de choices[0].message.content
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                _logger.LogWarning("Respuesta de NVIDIA AI no contiene 'choices' o está vacío");
                return AICompletionResult.Fail("La respuesta del proveedor de IA tiene un formato inesperado.");
            }

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var contentElement))
            {
                _logger.LogWarning("Respuesta de NVIDIA AI no contiene 'message.content' en choices[0]");
                return AICompletionResult.Fail("La respuesta del proveedor de IA tiene un formato inesperado.");
            }

            var content = contentElement.GetString() ?? string.Empty;

            // Si el modo thinking está activo y no se limpia, extraer reasoning_content
            if (_settings.EnableThinking && !_settings.ClearThinking &&
                message.TryGetProperty("reasoning_content", out var reasoningElement))
            {
                var reasoning = reasoningElement.GetString();
                if (!string.IsNullOrEmpty(reasoning))
                {
                    // Incluir razonamiento como metadata separada (prefijado)
                    content = $"[Razonamiento]\n{reasoning}\n\n[Respuesta]\n{content}";
                }
            }

            // Extraer métricas de uso de tokens
            var promptTokens = 0;
            var completionTokens = 0;
            var totalTokens = 0;

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt))
                    promptTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct))
                    completionTokens = ct.GetInt32();
                if (usage.TryGetProperty("total_tokens", out var tt))
                    totalTokens = tt.GetInt32();
            }

            return AICompletionResult.Ok(content, promptTokens, completionTokens, totalTokens);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Error al deserializar respuesta de NVIDIA AI");
            return AICompletionResult.Fail("La respuesta del proveedor de IA tiene un formato inesperado.");
        }
    }

    /// <summary>
    /// Valida que un valor entero esté dentro del rango especificado.
    /// Si está fuera de rango, registra un error y retorna el valor por defecto.
    /// </summary>
    private int ValidateRange(int value, int min, int max, int defaultValue, string propertyName)
    {
        if (value >= min && value <= max)
            return value;

        _logger.LogError(
            "Configuración NvidiaAI: {Property} = {Value} fuera del rango válido [{Min}, {Max}]. Usando valor por defecto: {Default}",
            propertyName, value, min, max, defaultValue);
        return defaultValue;
    }

    /// <summary>
    /// Valida que un valor double esté dentro del rango especificado.
    /// Si está fuera de rango, registra un error y retorna el valor por defecto.
    /// </summary>
    private double ValidateRange(double value, double min, double max, double defaultValue, string propertyName)
    {
        if (value >= min && value <= max)
            return value;

        _logger.LogError(
            "Configuración NvidiaAI: {Property} = {Value} fuera del rango válido [{Min}, {Max}]. Usando valor por defecto: {Default}",
            propertyName, value, min, max, defaultValue);
        return defaultValue;
    }
}
