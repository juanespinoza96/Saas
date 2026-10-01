using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SaasPOS.Api.Controllers.Tenants;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Property tests para NvidiaAIService usando FsCheck.
/// Valida invariantes del servicio con generadores personalizados.
/// **Validates: Requirements 1.7, 2.2, 2.6, 3.3, 3.4, 3.5, 3.10, 3.11**
/// </summary>
public class NvidiaAIPropertyTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handler HTTP mock que captura solicitudes y retorna respuestas configuradas.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return await _handler(request);
        }
    }

    /// <summary>
    /// Crea una respuesta HTTP exitosa con formato NVIDIA/OpenAI.
    /// </summary>
    private static HttpResponseMessage CreateSuccessResponse(
        string content = "respuesta", int promptTokens = 10, int completionTokens = 20, int totalTokens = 30)
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content },
                    finish_reason = "stop"
                }
            },
            usage = new
            {
                prompt_tokens = promptTokens,
                completion_tokens = completionTokens,
                total_tokens = totalTokens
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Crea el servicio NvidiaAIService con un handler mock y settings personalizados.
    /// </summary>
    private static (NvidiaAIService service, MockHttpMessageHandler handler) CreateService(
        NvidiaAISettings? settings = null,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? handlerFunc = null)
    {
        var effectiveSettings = settings ?? new NvidiaAISettings
        {
            ApiKey = "nvapi-test-key-12345",
            BaseUrl = "https://integrate.api.nvidia.com/v1",
            Model = "z-ai/glm-5.2"
        };

        var mockHandler = new MockHttpMessageHandler(
            handlerFunc ?? (_ => Task.FromResult(CreateSuccessResponse())));

        var httpClient = new HttpClient(mockHandler)
        {
            BaseAddress = new Uri(effectiveSettings.BaseUrl)
        };

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("NvidiaAI")).Returns(httpClient);

        var options = Options.Create(effectiveSettings);
        var logger = new Mock<ILogger<NvidiaAIService>>();

        var service = new NvidiaAIService(mockFactory.Object, options, logger.Object);
        return (service, mockHandler);
    }

    /// <summary>
    /// Mensajes de prueba válidos para enviar solicitudes.
    /// </summary>
    private static List<ChatMessage> CreateTestMessages(int count = 1)
    {
        var roles = new[] { "system", "user", "assistant" };
        var messages = new List<ChatMessage>();
        for (int i = 0; i < count; i++)
        {
            messages.Add(new ChatMessage
            {
                Role = roles[i % roles.Length],
                Content = $"Mensaje de prueba {i}"
            });
        }
        return messages;
    }

    // ─── Property 1: Validación de rangos numéricos aplica defaults ─────────────
    // **Validates: Requirements 3.3, 3.4**

    /// <summary>
    /// Property 1: Para cualquier valor fuera de rango válido, el sistema aplica defaults.
    /// Si temperature está fuera de [0,2], top_p fuera de [0,1], max_tokens fuera de [1,131072],
    /// el body enviado a NVIDIA contiene los valores por defecto.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Configuracion_FueraDeRango_AplicaDefaults()
    {
        // Generador de temperaturas fuera de rango [0, 2]
        var badTempGen = Gen.OneOf(
            Gen.Choose(-100, -1).Select(i => i * 0.1),   // negativos
            Gen.Choose(21, 100).Select(i => i * 0.1));    // > 2.0

        // Generador de topP fuera de rango [0, 1]
        var badTopPGen = Gen.OneOf(
            Gen.Choose(-100, -1).Select(i => i * 0.1),   // negativos
            Gen.Choose(11, 100).Select(i => i * 0.1));    // > 1.0

        // Generador de maxTokens fuera de rango [1, 131072]
        var badMaxTokensGen = Gen.OneOf(
            Gen.Choose(-1000, 0),                          // <= 0
            Gen.Choose(131073, 200000));                   // > 131072

        var gen = from temp in badTempGen
                  from topP in badTopPGen
                  from maxTokens in badMaxTokensGen
                  select (temp, topP, maxTokens);

        return Prop.ForAll(gen.ToArbitrary(), async tuple =>
        {
            var (badTemp, badTopP, badMaxTokens) = tuple;

            var settings = new NvidiaAISettings
            {
                ApiKey = "nvapi-test-key",
                BaseUrl = "https://test.nvidia.com/v1",
                Model = "test-model",
                Temperature = badTemp,
                TopP = badTopP,
                MaxTokens = badMaxTokens,
                TimeoutSeconds = 30
            };

            var (service, handler) = CreateService(settings);
            await service.ChatCompletionAsync(CreateTestMessages(), CancellationToken.None);

            // Verificar que el body contiene valores por defecto
            Assert.NotNull(handler.LastRequestBody);
            using var doc = JsonDocument.Parse(handler.LastRequestBody!);
            var root = doc.RootElement;

            var bodyTemp = root.GetProperty("temperature").GetDouble();
            var bodyTopP = root.GetProperty("top_p").GetDouble();
            var bodyMaxTokens = root.GetProperty("max_tokens").GetInt32();

            // Defaults según NvidiaAIService: temperature=1.0, topP=0.95, maxTokens=16384
            Assert.Equal(1.0, bodyTemp, precision: 5);
            Assert.Equal(0.95, bodyTopP, precision: 5);
            Assert.Equal(16384, bodyMaxTokens);
        });
    }

    // ─── Property 2: Parámetros opcionales sobreescriben valores por defecto ────
    // **Validates: Requirements 2.2**

    /// <summary>
    /// Property 2: Para cualquier parámetro opcional válido, el request body enviado
    /// contiene esos valores en lugar de los defaults de configuración.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ParametrosOpcionales_SobreescribenDefaults()
    {
        // Generadores de valores válidos para parámetros opcionales
        var validTempGen = Gen.Choose(0, 200).Select(i => i * 0.01);      // [0.0, 2.0]
        var validMaxTokensGen = Gen.Choose(1, 16384);                      // [1, 16384]
        var validTopPGen = Gen.Choose(0, 100).Select(i => i * 0.01);      // [0.0, 1.0]

        var gen = from temp in validTempGen
                  from maxTokens in validMaxTokensGen
                  from topP in validTopPGen
                  select (temp, maxTokens, topP);

        return Prop.ForAll(gen.ToArbitrary(), async tuple =>
        {
            var (optTemp, optMaxTokens, optTopP) = tuple;

            var (service, handler) = CreateService();
            await service.ChatCompletionAsync(
                CreateTestMessages(),
                temperature: optTemp,
                maxTokens: optMaxTokens,
                topP: optTopP);

            // Verificar que el body contiene los valores opcionales enviados
            Assert.NotNull(handler.LastRequestBody);
            using var doc = JsonDocument.Parse(handler.LastRequestBody!);
            var root = doc.RootElement;

            var bodyTemp = root.GetProperty("temperature").GetDouble();
            var bodyTopP = root.GetProperty("top_p").GetDouble();
            var bodyMaxTokens = root.GetProperty("max_tokens").GetInt32();

            Assert.Equal(optTemp, bodyTemp, precision: 5);
            Assert.Equal(optTopP, bodyTopP, precision: 5);
            Assert.Equal(optMaxTokens, bodyMaxTokens);
        });
    }

    // ─── Property 3: Cualquier fallo retorna AICompletionResult consistente ─────
    // **Validates: Requirements 3.10, 3.11**

    /// <summary>
    /// Property 3: Para cualquier tipo de fallo (timeout, error de red, error HTTP),
    /// el resultado tiene Success=false, Content="", tokens=0, ErrorMessage no-null
    /// y NO contiene la API key.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Fallos_RetornanResultadoConsistente()
    {
        // Generador de distintos tipos de fallos
        var failureGen = Gen.Choose(0, 3);

        return Prop.ForAll(failureGen.ToArbitrary(), async failureType =>
        {
            const string apiKey = "nvapi-secret-key-super-private-12345";

            var settings = new NvidiaAISettings
            {
                ApiKey = apiKey,
                BaseUrl = "https://test.nvidia.com/v1",
                Model = "test-model",
                TimeoutSeconds = 30
            };

            Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc = failureType switch
            {
                0 => _ => throw new TaskCanceledException("timeout",
                    new TimeoutException("Request timed out")),
                1 => _ => throw new HttpRequestException("Network error"),
                2 => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("Server Error")
                }),
                _ => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("Bad Request")
                })
            };

            var (service, _) = CreateService(settings, handlerFunc);
            var result = await service.ChatCompletionAsync(CreateTestMessages(), CancellationToken.None);

            // Invariantes del resultado de fallo
            Assert.False(result.Success);
            Assert.Equal(string.Empty, result.Content);
            Assert.Equal(0, result.PromptTokens);
            Assert.Equal(0, result.CompletionTokens);
            Assert.Equal(0, result.TotalTokens);
            Assert.NotNull(result.ErrorMessage);
            // La API key NUNCA debe aparecer en el mensaje de error
            Assert.DoesNotContain(apiKey, result.ErrorMessage);
        });
    }

    // ─── Property 4: Estructura del body de solicitud a NVIDIA es completa ──────
    // **Validates: Requirements 1.7, 2.6**

    /// <summary>
    /// Property 4: Para cualquier lista válida de mensajes (1-50 mensajes, roles válidos,
    /// contenido ≤ 32000 chars), el JSON body tiene exactamente: model, messages,
    /// temperature, top_p, max_tokens, stream (false), chat_template_kwargs (enable_thinking, clear_thinking).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EstructuraBody_EsCompleta()
    {
        var roles = new[] { "system", "user", "assistant" };

        // Generador de mensajes válidos (1 a 10 para mantener velocidad)
        var messageGen = from count in Gen.Choose(1, 10)
                         from msgs in Gen.ListOf(count,
                             from role in Gen.Elements(roles)
                             from contentLen in Gen.Choose(1, 100)
                             from content in Gen.Elements("Hola", "¿Cómo estás?", "Ayuda", "Test", "Mensaje")
                             select new ChatMessage { Role = role, Content = content })
                         select msgs.ToList();

        return Prop.ForAll(messageGen.ToArbitrary(), async messages =>
        {
            var (service, handler) = CreateService();
            await service.ChatCompletionAsync(messages, CancellationToken.None);

            Assert.NotNull(handler.LastRequestBody);
            using var doc = JsonDocument.Parse(handler.LastRequestBody!);
            var root = doc.RootElement;

            // Verificar que contiene todas las propiedades requeridas
            Assert.True(root.TryGetProperty("model", out var model));
            Assert.False(string.IsNullOrEmpty(model.GetString()));

            Assert.True(root.TryGetProperty("messages", out var msgArray));
            Assert.Equal(JsonValueKind.Array, msgArray.ValueKind);
            Assert.Equal(messages.Count, msgArray.GetArrayLength());

            Assert.True(root.TryGetProperty("temperature", out var temp));
            Assert.Equal(JsonValueKind.Number, temp.ValueKind);

            Assert.True(root.TryGetProperty("top_p", out var topP));
            Assert.Equal(JsonValueKind.Number, topP.ValueKind);

            Assert.True(root.TryGetProperty("max_tokens", out var maxTokens));
            Assert.Equal(JsonValueKind.Number, maxTokens.ValueKind);

            Assert.True(root.TryGetProperty("stream", out var stream));
            Assert.False(stream.GetBoolean());

            Assert.True(root.TryGetProperty("chat_template_kwargs", out var kwargs));
            Assert.Equal(JsonValueKind.Object, kwargs.ValueKind);
            Assert.True(kwargs.TryGetProperty("enable_thinking", out var enableThinking));
            Assert.Equal(JsonValueKind.True, enableThinking.ValueKind);
            Assert.True(kwargs.TryGetProperty("clear_thinking", out var clearThinking));
            Assert.Equal(JsonValueKind.False, clearThinking.ValueKind);

            // Verificar que cada mensaje tiene role y content
            foreach (var msg in msgArray.EnumerateArray())
            {
                Assert.True(msg.TryGetProperty("role", out _));
                Assert.True(msg.TryGetProperty("content", out _));
            }
        });
    }

    // ─── Property 5: Deserialización de respuesta extrae datos correctamente ────
    // **Validates: Requirements 3.5**

    /// <summary>
    /// Property 5: Para cualquier respuesta JSON válida con choices[0].message.content
    /// y usage tokens, la deserialización produce AICompletionResult correcto.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Deserializacion_ExtraeDatosCorrectamente()
    {
        // Generador de contenido y tokens válidos
        var gen = from content in Arb.Generate<NonEmptyString>()
                  from promptTokens in Gen.Choose(0, 10000)
                  from completionTokens in Gen.Choose(0, 10000)
                  select (content: content.Get, promptTokens, completionTokens);

        return Prop.ForAll(gen.ToArbitrary(), async tuple =>
        {
            var (expectedContent, expectedPromptTokens, expectedCompletionTokens) = tuple;
            var expectedTotalTokens = expectedPromptTokens + expectedCompletionTokens;

            // Construir respuesta mock con los valores generados
            Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc = _ =>
            {
                var responseJson = JsonSerializer.Serialize(new
                {
                    id = "chatcmpl-test",
                    @object = "chat.completion",
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            message = new { role = "assistant", content = expectedContent },
                            finish_reason = "stop"
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = expectedPromptTokens,
                        completion_tokens = expectedCompletionTokens,
                        total_tokens = expectedTotalTokens
                    }
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
            };

            var (service, _) = CreateService(handlerFunc: handlerFunc);
            var result = await service.ChatCompletionAsync(CreateTestMessages(), CancellationToken.None);

            // Verificar que la deserialización extrajo los valores correctos
            Assert.True(result.Success);
            Assert.Equal(expectedContent, result.Content);
            Assert.Equal(expectedPromptTokens, result.PromptTokens);
            Assert.Equal(expectedCompletionTokens, result.CompletionTokens);
            Assert.Equal(expectedTotalTokens, result.TotalTokens);
            Assert.Null(result.ErrorMessage);
        });
    }

    // ─── Property 6: Códigos HTTP 4xx genéricos incluyen el código en ErrorMessage
    // **Validates: Requirements 3.10**

    /// <summary>
    /// Property 6: Para cualquier código HTTP 4xx que NO sea 401, 403, 429,
    /// el ErrorMessage contiene el código numérico.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CodigosHttp4xx_IncluyenCodigoEnErrorMessage()
    {
        // Generador de códigos 4xx excluyendo 401, 403, 429
        var excluded = new HashSet<int> { 401, 403, 429 };
        var validCodes = Enumerable.Range(400, 100)
            .Where(c => !excluded.Contains(c))
            .ToArray();

        var codeGen = Gen.Elements(validCodes);

        return Prop.ForAll(codeGen.ToArbitrary(), async statusCode =>
        {
            Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc = _ =>
                Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode)
                {
                    Content = new StringContent($"Error {statusCode}")
                });

            var (service, _) = CreateService(handlerFunc: handlerFunc);
            var result = await service.ChatCompletionAsync(CreateTestMessages(), CancellationToken.None);

            // Verificar que el resultado es un fallo y contiene el código
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains(statusCode.ToString(), result.ErrorMessage);
        });
    }

    // ─── Helpers para Properties 9 y 10 (nivel controller) ──────────────────────

    /// <summary>
    /// Crea un AIController con mocks configurados y retorna los mocks para verificación.
    /// </summary>
    private static (AIController controller, Mock<IAIService> aiService, Mock<IAuditService> auditService, Mock<ILogger<AIController>> logger) CrearControllerConMocks()
    {
        var aiService = new Mock<IAIService>();
        var guard = new Mock<ISubscriptionGuard>();
        var tenantContext = new Mock<ITenantContext>();
        var settings = Options.Create(new NvidiaAISettings
        {
            ApiKey = "nvapi-test-key",
            BaseUrl = "https://test.nvidia.com/v1",
            Model = "test-model"
        });
        var auditService = new Mock<IAuditService>();
        var logger = new Mock<ILogger<AIController>>();

        // Configurar tenant context con ComercioId válido
        tenantContext.Setup(t => t.ComercioId).Returns(1);
        // SubscriptionGuard retorna true
        guard.Setup(g => g.CanUseAIAsync(It.IsAny<int>())).ReturnsAsync(true);
        // IAIService.IsAvailableAsync retorna true
        aiService.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var controller = new AIController(
            aiService.Object, guard.Object, tenantContext.Object,
            settings, auditService.Object, logger.Object,
            new Mock<IChatBusinessIntelligenceService>().Object, null!,
            CreateDevEnvironment().Object);

        // Configurar HttpContext con claims JWT
        var claims = new[] { new Claim("sub", "1"), new Claim("comercio_id", "1") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        return (controller, aiService, auditService, logger);
    }

    // ─── Property 9: Auditoría registra metadatos correctos sin contenido de mensajes
    // **Validates: Requirements 8.1, 8.2, 8.3**

    /// <summary>
    /// Property 9: Para cualquier resultado de una solicitud de IA (exitoso o fallido)
    /// y para cualquier contenido de mensajes del usuario, el registro en LogsAuditoria
    /// contiene exclusivamente metadatos operativos (tokens, modelo, tipo de error) y NUNCA
    /// el contenido de los mensajes del usuario ni de la respuesta de IA.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Auditoria_RegistraMetadatos_SinContenidoDeMensajes()
    {
        // Generador de contenido con prefijo único que NUNCA aparecerá en metadatos JSON
        // Usamos "XXYYZZ" como marcador único seguido de un número aleatorio
        var messageContentGen = Gen.Choose(10000, 99999).Select(id =>
            $"XXYYZZ{id}AABBCC");
        var aiContentGen = Gen.Choose(10000, 99999).Select(id =>
            $"QQRRSS{id}DDMMFF");
        // Generador de tokens
        var tokensGen = Gen.Choose(1, 10000);
        // Generador de caso: exitoso o fallido
        var successGen = Arb.Generate<bool>();

        var gen = from userContent in messageContentGen
                  from aiResponseContent in aiContentGen
                  from promptTokens in tokensGen
                  from completionTokens in tokensGen
                  from isSuccess in successGen
                  select (userContent, aiResponseContent, promptTokens, completionTokens, isSuccess);

        return Prop.ForAll(gen.ToArbitrary(), async tuple =>
        {
            var (userContent, aiResponseContent, promptTokens, completionTokens, isSuccess) = tuple;
            var totalTokens = promptTokens + completionTokens;

            var (controller, aiService, auditService, _) = CrearControllerConMocks();

            // Capturar los argumentos pasados a RegistrarAsync
            object? capturedValoresNuevos = null;
            string? capturedAccion = null;
            object? capturedValoresAnteriores = null;

            auditService.Setup(a => a.RegistrarAsync(
                It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>()))
                .Callback<int, int?, string, string, string, object?, object?>(
                    (_, _, accion, _, _, valAnt, valNuevos) =>
                    {
                        capturedAccion = accion;
                        capturedValoresAnteriores = valAnt;
                        capturedValoresNuevos = valNuevos;
                    })
                .Returns(Task.CompletedTask);

            if (isSuccess)
            {
                // Configurar IAIService para retornar éxito con contenido de IA
                aiService.Setup(s => s.ChatCompletionAsync(
                    It.IsAny<List<ChatMessage>>(),
                    It.IsAny<double?>(), It.IsAny<int?>(), It.IsAny<double?>(),
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AICompletionResult.Ok(aiResponseContent, promptTokens, completionTokens, totalTokens));
            }
            else
            {
                // Configurar IAIService para retornar error
                aiService.Setup(s => s.ChatCompletionAsync(
                    It.IsAny<List<ChatMessage>>(),
                    It.IsAny<double?>(), It.IsAny<int?>(), It.IsAny<double?>(),
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AICompletionResult.Fail("Error de prueba: timeout"));
            }

            // Crear request con contenido aleatorio del usuario
            var request = new AIChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = userContent }
                }
            };

            // Ejecutar
            await controller.Chat(request, CancellationToken.None);

            // Verificar que se llamó a RegistrarAsync
            auditService.Verify(a => a.RegistrarAsync(
                It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>()), Times.Once);

            // Verificar que valoresAnteriores es null
            Assert.Null(capturedValoresAnteriores);

            // Serializar valoresNuevos para inspeccionar su contenido
            var serializedNuevos = JsonSerializer.Serialize(capturedValoresNuevos);

            // NUNCA debe contener el contenido del mensaje del usuario
            Assert.DoesNotContain(userContent, serializedNuevos);

            if (isSuccess)
            {
                // Para éxito: acción es AI_ChatCompletion y contiene tokens/modelo
                Assert.Equal("AI_ChatCompletion", capturedAccion);
                Assert.Contains("prompt_tokens", serializedNuevos);
                Assert.Contains("completion_tokens", serializedNuevos);
                Assert.Contains("total_tokens", serializedNuevos);
                Assert.Contains("model", serializedNuevos);
                // NUNCA debe contener el contenido de la respuesta de IA
                Assert.DoesNotContain(aiResponseContent, serializedNuevos);
            }
            else
            {
                // Para error: acción es AI_ChatCompletion_Error y contiene error_type
                Assert.Equal("AI_ChatCompletion_Error", capturedAccion);
                Assert.Contains("error_type", serializedNuevos);
                // NUNCA debe contener el contenido del mensaje del usuario
                Assert.DoesNotContain(userContent, serializedNuevos);
            }
        });
    }

    // ─── Property 10: Fallo de auditoría no interrumpe la entrega de respuesta ──
    // **Validates: Requirements 8.5**

    /// <summary>
    /// Property 10: Para cualquier excepción que ocurra durante el registro en LogsAuditoria
    /// posterior a una solicitud de IA exitosa, el sistema entrega la respuesta de IA
    /// al usuario con HTTP 200 sin interrupción.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FalloAuditoria_NoInterrumpeEntregaDeRespuesta()
    {
        // Generadores de contenido y tokens para la respuesta exitosa
        var contentGen = Arb.Generate<NonEmptyString>().Select(s => s.Get);
        var tokensGen = Gen.Choose(1, 10000);
        // Generador de tipos de excepción para simular fallo de auditoría
        var exceptionGen = Gen.Elements<Exception>(
            new InvalidOperationException("DB connection lost"),
            new TimeoutException("Audit timeout"),
            new IOException("Disk full"),
            new Exception("Unexpected audit failure"),
            new NullReferenceException("Audit context null"));

        var gen = from aiContent in contentGen
                  from promptTokens in tokensGen
                  from completionTokens in tokensGen
                  from exception in exceptionGen
                  select (aiContent, promptTokens, completionTokens, exception);

        return Prop.ForAll(gen.ToArbitrary(), async tuple =>
        {
            var (aiContent, promptTokens, completionTokens, auditException) = tuple;
            var totalTokens = promptTokens + completionTokens;

            var (controller, aiService, auditService, logger) = CrearControllerConMocks();

            // Configurar IAIService para retornar éxito
            aiService.Setup(s => s.ChatCompletionAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<double?>(), It.IsAny<int?>(), It.IsAny<double?>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(AICompletionResult.Ok(aiContent, promptTokens, completionTokens, totalTokens));

            // Configurar IAuditService para LANZAR excepción
            auditService.Setup(a => a.RegistrarAsync(
                It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>()))
                .ThrowsAsync(auditException);

            // Crear request válido
            var request = new AIChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = "Mensaje de prueba" }
                }
            };

            // Ejecutar
            var result = await controller.Chat(request, CancellationToken.None);

            // Verificar que la respuesta es HTTP 200 (OkObjectResult)
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, okResult.StatusCode);

            // Verificar que la respuesta contiene los datos correctos de IA
            var response = Assert.IsType<AIChatResponse>(okResult.Value);
            Assert.Equal(aiContent, response.Content);
            Assert.Equal(promptTokens, response.PromptTokens);
            Assert.Equal(completionTokens, response.CompletionTokens);
            Assert.Equal(totalTokens, response.TotalTokens);

            // Verificar que el logger registró el fallo de auditoría como Warning
            logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        });
    }

    /// <summary>
    /// Crea un mock de IWebHostEnvironment configurado como Development para tests.
    /// </summary>
    private static Mock<IWebHostEnvironment> CreateDevEnvironment()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        return env;
    }
}
