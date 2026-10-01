using System.Reflection;
using System.Security.Claims;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SaasPOS.Api.Controllers.Tenants;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Configuration;

namespace SaasPOS.Tests.Controllers;

/// <summary>
/// Property-based tests para la validación de input del AIController (Property 8).
/// Verifica que solicitudes inválidas retornan HTTP 400 con mensaje específico.
/// **Validates: Requirements 6.3, 10.4, 10.5, 10.6, 10.7**
///
/// Property 8: "For any AIChatRequest que contenga al menos una violación de las reglas
/// de validación (lista de mensajes vacía/nula, más de 50 mensajes, un mensaje con Role
/// distinto de 'system'/'user'/'assistant', un mensaje con Content vacío o > 32000
/// caracteres, Temperature fuera de [0.0, 2.0], MaxTokens fuera de [1, 16384], o TopP
/// fuera de [0.0, 1.0]), el endpoint SHALL retornar HTTP 400 con un mensaje indicando
/// el motivo específico."
/// </summary>
public class AIControllerTests
{
    // --- Métodos auxiliares para crear el controlador con dependencias mockeadas ---

    /// <summary>
    /// Crea una instancia del AIController con mocks configurados para pasar autenticación y plan.
    /// </summary>
    private static AIController CrearController()
    {
        var aiService = new Mock<IAIService>();
        var guard = new Mock<ISubscriptionGuard>();
        var tenantContext = new Mock<ITenantContext>();
        var settings = Options.Create(new NvidiaAISettings());
        var auditService = new Mock<IAuditService>();
        var logger = new Mock<ILogger<AIController>>();
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        // ITenantContext retorna un ComercioId válido para pasar la verificación de auth
        tenantContext.Setup(t => t.ComercioId).Returns(1);
        // SubscriptionGuard retorna true para pasar la verificación de plan
        guard.Setup(g => g.CanUseAIAsync(It.IsAny<int>())).ReturnsAsync(true);

        return new AIController(aiService.Object, guard.Object, tenantContext.Object, settings, auditService.Object, logger.Object, new Mock<IChatBusinessIntelligenceService>().Object, null!, environment.Object);
    }

    /// <summary>
    /// Crea un mock de IWebHostEnvironment configurado como Development.
    /// </summary>
    private static Mock<IWebHostEnvironment> CreateDevEnvironment()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        return env;
    }

    /// <summary>
    /// Crea un ChatMessage válido con rol y contenido apropiados.
    /// </summary>
    private static ChatMessage MensajeValido(string rol = "user", string contenido = "Hola")
    {
        return new ChatMessage { Role = rol, Content = contenido };
    }

    /// <summary>
    /// Crea un AIChatRequest válido con un solo mensaje.
    /// </summary>
    private static AIChatRequest RequestValido()
    {
        return new AIChatRequest
        {
            Messages = new List<ChatMessage> { MensajeValido() }
        };
    }

    // --- Property Tests ---

    /// <summary>
    /// Property 8.1: Para cualquier request con Messages = null o lista vacía, retorna 400.
    /// Generamos aleatoriamente null o lista vacía.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property_EmptyMessages_Returns400()
    {
        // Generador: elegimos entre null y lista vacía
        var gen = Gen.Elements(true, false).Select(useNull =>
        {
            var request = new AIChatRequest();
            if (useNull)
                request.Messages = null!;
            else
                request.Messages = new List<ChatMessage>();
            return request;
        });

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var controller = CrearController();
            var result = controller.Chat(request, CancellationToken.None).GetAwaiter().GetResult();

            var badRequest = result as BadRequestObjectResult;
            Assert.NotNull(badRequest);
            Assert.Equal(400, badRequest.StatusCode);
            // Verificar que tiene un mensaje de error
            Assert.NotNull(badRequest.Value);
        });
    }

    /// <summary>
    /// Property 8.2: Para cualquier request con más de 50 mensajes, retorna 400.
    /// Generamos una cantidad aleatoria entre 51 y 100.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property_TooManyMessages_Returns400()
    {
        var gen = Gen.Choose(51, 100).Select(count =>
        {
            var messages = Enumerable.Range(0, count)
                .Select(_ => MensajeValido())
                .ToList();
            return new AIChatRequest { Messages = messages };
        });

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var controller = CrearController();
            var result = controller.Chat(request, CancellationToken.None).GetAwaiter().GetResult();

            var badRequest = result as BadRequestObjectResult;
            Assert.NotNull(badRequest);
            Assert.Equal(400, badRequest.StatusCode);
            Assert.NotNull(badRequest.Value);
        });
    }

    /// <summary>
    /// Property 8.3: Para cualquier mensaje con rol NO válido (distinto de "system", "user", "assistant"),
    /// retorna 400.
    /// Generamos roles inválidos aleatorios.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property_InvalidRole_Returns400()
    {
        // Roles inválidos: variantes de capitalización, nombres aleatorios, GUIDs
        var invalidRoles = Gen.OneOf(
            Gen.Elements("admin", "bot", "manager", "superuser", "invalid", "USER", "System",
                         "Assistant", "SYSTEM", "ASSISTANT", "moderator", "owner", ""),
            Gen.Fresh(() => Guid.NewGuid().ToString())
        );

        var gen = invalidRoles.Select(role =>
        {
            var request = new AIChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = role, Content = "contenido válido" }
                }
            };
            return request;
        });

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var controller = CrearController();
            var result = controller.Chat(request, CancellationToken.None).GetAwaiter().GetResult();

            var badRequest = result as BadRequestObjectResult;
            Assert.NotNull(badRequest);
            Assert.Equal(400, badRequest.StatusCode);
            Assert.NotNull(badRequest.Value);
        });
    }

    /// <summary>
    /// Property 8.4: Para cualquier mensaje con Content vacío o null, retorna 400.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property_EmptyContent_Returns400()
    {
        // Generamos contenido vacío: null, string vacío, o solo espacios (empty después de check)
        var gen = Gen.Elements<string?>(null, "", "").Select(content =>
        {
            var request = new AIChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = content! }
                }
            };
            return request;
        });

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var controller = CrearController();
            var result = controller.Chat(request, CancellationToken.None).GetAwaiter().GetResult();

            var badRequest = result as BadRequestObjectResult;
            Assert.NotNull(badRequest);
            Assert.Equal(400, badRequest.StatusCode);
            Assert.NotNull(badRequest.Value);
        });
    }

    /// <summary>
    /// Property 8.5: Para cualquier mensaje con Content > 32000 caracteres, retorna 400.
    /// Generamos longitudes aleatorias entre 32001 y 35000.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property_ContentTooLong_Returns400()
    {
        var gen = Gen.Choose(32001, 35000).Select(length =>
        {
            var content = new string('a', length);
            var request = new AIChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = content }
                }
            };
            return request;
        });

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var controller = CrearController();
            var result = controller.Chat(request, CancellationToken.None).GetAwaiter().GetResult();

            var badRequest = result as BadRequestObjectResult;
            Assert.NotNull(badRequest);
            Assert.Equal(400, badRequest.StatusCode);
            Assert.NotNull(badRequest.Value);
        });
    }

    /// <summary>
    /// Property 8.6: Para cualquier Temperature fuera de [0.0, 2.0], retorna 400.
    /// Generamos valores negativos o mayores a 2.0.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property_TemperatureOutOfRange_Returns400()
    {
        var gen = Gen.OneOf(
            // Valores negativos: -10.0 a -0.1
            Gen.Choose(-100, -1).Select(x => x / 10.0),
            // Valores mayores a 2.0: 2.1 a 10.0
            Gen.Choose(21, 100).Select(x => x / 10.0)
        ).Select(temp =>
        {
            var request = RequestValido();
            request.Temperature = temp;
            return request;
        });

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var controller = CrearController();
            var result = controller.Chat(request, CancellationToken.None).GetAwaiter().GetResult();

            var badRequest = result as BadRequestObjectResult;
            Assert.NotNull(badRequest);
            Assert.Equal(400, badRequest.StatusCode);
            Assert.NotNull(badRequest.Value);
        });
    }

    /// <summary>
    /// Property 8.7: Para cualquier MaxTokens fuera de [1, 16384], retorna 400.
    /// Generamos valores <= 0 o > 16384.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property_MaxTokensOutOfRange_Returns400()
    {
        var gen = Gen.OneOf(
            // Valores menores o iguales a 0: -1000 a 0
            Gen.Choose(-1000, 0),
            // Valores mayores a 16384: 16385 a 100000
            Gen.Choose(16385, 100000)
        ).Select(tokens =>
        {
            var request = RequestValido();
            request.MaxTokens = tokens;
            return request;
        });

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var controller = CrearController();
            var result = controller.Chat(request, CancellationToken.None).GetAwaiter().GetResult();

            var badRequest = result as BadRequestObjectResult;
            Assert.NotNull(badRequest);
            Assert.Equal(400, badRequest.StatusCode);
            Assert.NotNull(badRequest.Value);
        });
    }

    /// <summary>
    /// Property 8.8: Para cualquier TopP fuera de [0.0, 1.0], retorna 400.
    /// Generamos valores negativos o mayores a 1.0.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property_TopPOutOfRange_Returns400()
    {
        var gen = Gen.OneOf(
            // Valores negativos: -10.0 a -0.1
            Gen.Choose(-100, -1).Select(x => x / 10.0),
            // Valores mayores a 1.0: 1.1 a 10.0
            Gen.Choose(11, 100).Select(x => x / 10.0)
        ).Select(topP =>
        {
            var request = RequestValido();
            request.TopP = topP;
            return request;
        });

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var controller = CrearController();
            var result = controller.Chat(request, CancellationToken.None).GetAwaiter().GetResult();

            var badRequest = result as BadRequestObjectResult;
            Assert.NotNull(badRequest);
            Assert.Equal(400, badRequest.StatusCode);
            Assert.NotNull(badRequest.Value);
        });
    }

    // ====================================================================
    // UNIT TESTS - Escenarios de integración del controller
    // **Validates: Requirements 6.5, 6.6, 4.4, 7.2, 7.3, 7.4**
    // ====================================================================

    /// <summary>
    /// Crea una instancia del AIController con mocks y configura User claims para tests unitarios.
    /// Permite configurar el comportamiento de cada dependencia.
    /// </summary>
    private static (AIController controller, Mock<IAIService> aiService, Mock<ISubscriptionGuard> guard, Mock<IAuditService> auditService)
        CrearControllerConMocks(NvidiaAISettings? settings = null)
    {
        var aiService = new Mock<IAIService>();
        var guard = new Mock<ISubscriptionGuard>();
        var tenantContext = new Mock<ITenantContext>();
        var aiSettings = settings ?? new NvidiaAISettings { ApiKey = "test-key", Model = "z-ai/glm-5.2" };
        var options = Options.Create(aiSettings);
        var auditService = new Mock<IAuditService>();
        var logger = new Mock<ILogger<AIController>>();

        // Configurar ITenantContext con ComercioId válido
        tenantContext.Setup(t => t.ComercioId).Returns(1);

        var controller = new AIController(aiService.Object, guard.Object, tenantContext.Object, options, auditService.Object, logger.Object, new Mock<IChatBusinessIntelligenceService>().Object, null!, CreateDevEnvironment().Object);

        // Configurar HttpContext con claims del usuario (necesario para GetUsuarioId())
        var claims = new List<Claim>
        {
            new Claim("sub", "42"),
            new Claim(ClaimTypes.Role, "Dueño")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        return (controller, aiService, guard, auditService);
    }

    /// <summary>
    /// Test: El endpoint Chat retorna HTTP 503 cuando el servicio de IA no está disponible.
    /// Verifica que IsAvailableAsync = false produce Service Unavailable.
    /// </summary>
    [Fact]
    public async Task Chat_Retorna503_CuandoServicioNoDisponible()
    {
        // Arrange
        var (controller, aiService, guard, _) = CrearControllerConMocks();
        guard.Setup(g => g.CanUseAIAsync(It.IsAny<int>())).ReturnsAsync(true);
        aiService.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var request = RequestValido();

        // Act
        var result = await controller.Chat(request, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    /// <summary>
    /// Test: El endpoint Chat retorna HTTP 403 cuando el plan del comercio no tiene acceso a IA.
    /// Verifica que CanUseAIAsync = false produce Forbidden.
    /// </summary>
    [Fact]
    public async Task Chat_Retorna403_CuandoPlanNoTieneAcceso()
    {
        // Arrange
        var (controller, _, guard, _) = CrearControllerConMocks();
        guard.Setup(g => g.CanUseAIAsync(It.IsAny<int>())).ReturnsAsync(false);

        var request = RequestValido();

        // Act
        var result = await controller.Chat(request, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    /// <summary>
    /// Test: El endpoint Chat retorna HTTP 502 cuando el servicio de IA falla (Success = false).
    /// Verifica que ChatCompletionAsync con fallo produce Bad Gateway.
    /// </summary>
    [Fact]
    public async Task Chat_Retorna502_CuandoServicioFalla()
    {
        // Arrange
        var (controller, aiService, guard, _) = CrearControllerConMocks();
        guard.Setup(g => g.CanUseAIAsync(It.IsAny<int>())).ReturnsAsync(true);
        aiService.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        aiService.Setup(s => s.ChatCompletionAsync(
            It.IsAny<List<ChatMessage>>(),
            It.IsAny<double?>(),
            It.IsAny<int?>(),
            It.IsAny<double?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(AICompletionResult.Fail("Error interno del proveedor de IA"));

        var request = RequestValido();

        // Act
        var result = await controller.Chat(request, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, statusResult.StatusCode);
    }

    /// <summary>
    /// Test: El endpoint Health retorna "healthy" cuando el servicio está disponible y ApiKey está configurada.
    /// Verifica el escenario ideal de health check.
    /// </summary>
    [Fact]
    public async Task Health_RetornaHealthy_CuandoServicioDisponible()
    {
        // Arrange
        var (controller, aiService, guard, _) = CrearControllerConMocks(new NvidiaAISettings
        {
            ApiKey = "nvidia-api-key-valida",
            Model = "z-ai/glm-5.2"
        });
        guard.Setup(g => g.CanUseAIAsync(It.IsAny<int>())).ReturnsAsync(true);
        aiService.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await controller.Health();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AIHealthResponse>(okResult.Value);
        Assert.Equal("healthy", response.Status);
        Assert.Equal("z-ai/glm-5.2", response.Model);
        Assert.Null(response.Message);
    }

    /// <summary>
    /// Test: El endpoint Health retorna "unhealthy" cuando IsAvailableAsync retorna false y ApiKey está configurada.
    /// Verifica que el servicio no respondiendo produce estado unhealthy.
    /// </summary>
    [Fact]
    public async Task Health_RetornaUnhealthy_CuandoServicioNoResponde()
    {
        // Arrange
        var (controller, aiService, guard, _) = CrearControllerConMocks(new NvidiaAISettings
        {
            ApiKey = "nvidia-api-key-valida",
            Model = "z-ai/glm-5.2"
        });
        guard.Setup(g => g.CanUseAIAsync(It.IsAny<int>())).ReturnsAsync(true);
        aiService.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var result = await controller.Health();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AIHealthResponse>(okResult.Value);
        Assert.Equal("unhealthy", response.Status);
        Assert.Equal("z-ai/glm-5.2", response.Model);
        Assert.NotNull(response.Message);
    }

    /// <summary>
    /// Test: El endpoint Health retorna "unconfigured" cuando ApiKey está vacía.
    /// Verifica que la ausencia de API key produce estado unconfigured.
    /// </summary>
    [Fact]
    public async Task Health_RetornaUnconfigured_CuandoApiKeyVacia()
    {
        // Arrange
        var (controller, _, guard, _) = CrearControllerConMocks(new NvidiaAISettings
        {
            ApiKey = "",
            Model = "z-ai/glm-5.2"
        });
        guard.Setup(g => g.CanUseAIAsync(It.IsAny<int>())).ReturnsAsync(true);

        // Act
        var result = await controller.Health();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AIHealthResponse>(okResult.Value);
        Assert.Equal("unconfigured", response.Status);
        Assert.Null(response.Model);
        Assert.NotNull(response.Message);
    }

    /// <summary>
    /// Test: Verifica que el AIController tiene aplicado el atributo [EnableRateLimiting("AIRateLimit")].
    /// Esto confirma que el rate limiting está configurado a nivel de controller.
    /// </summary>
    [Fact]
    public void Controller_TieneAtributoRateLimiting()
    {
        // Arrange & Act
        var attribute = typeof(AIController)
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        // Assert - verificar que el atributo existe y tiene la política correcta
        Assert.NotNull(attribute);
        Assert.Equal("AIRateLimit", attribute.PolicyName);
    }
}
