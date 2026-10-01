using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Api.Middleware;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Tests.Middleware;

/// <summary>
/// Tests unitarios para TimezoneResolutionMiddleware.
/// Valida extracción del header X-Timezone, límite de 64 chars,
/// resolución de zona horaria y manejo de errores.
/// </summary>
public class TimezoneResolutionMiddlewareTests
{
    private readonly Mock<ITimezoneResolver> _resolverMock;
    private readonly Mock<ILogger<TimezoneResolutionMiddleware>> _loggerMock;
    private bool _nextCalled;

    public TimezoneResolutionMiddlewareTests()
    {
        _resolverMock = new Mock<ITimezoneResolver>();
        _loggerMock = new Mock<ILogger<TimezoneResolutionMiddleware>>();
        _nextCalled = false;
    }

    /// <summary>
    /// Crea el middleware con las dependencias mockeadas.
    /// </summary>
    private TimezoneResolutionMiddleware CreateMiddleware()
    {
        return new TimezoneResolutionMiddleware(_ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        }, _loggerMock.Object);
    }

    /// <summary>
    /// Crea un HttpContext con el servicio ITimezoneResolver registrado.
    /// </summary>
    private DefaultHttpContext CreateHttpContext(ClaimsPrincipal? user = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_resolverMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        if (user != null)
            context.User = user;

        return context;
    }

    /// <summary>
    /// Crea un ClaimsPrincipal con claims sub y comercio_id.
    /// </summary>
    private static ClaimsPrincipal CreateUser(int usuarioId, int comercioId)
    {
        var claims = new List<Claim>
        {
            new("sub", usuarioId.ToString()),
            new("comercio_id", comercioId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestScheme");
        return new ClaimsPrincipal(identity);
    }

    // ── Extracción del header X-Timezone ────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ConHeaderXTimezone_PasaValorAlResolver()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(CreateUser(1, 10));
        context.Request.Headers["X-Timezone"] = "America/Guayaquil";

        _resolverMock
            .Setup(r => r.ResolveAsync(1, 10, "America/Guayaquil"))
            .ReturnsAsync("America/Guayaquil");

        // Act
        await middleware.InvokeAsync(context);

        // Assert — verifica que el resolver recibió el header correctamente
        _resolverMock.Verify(r => r.ResolveAsync(1, 10, "America/Guayaquil"), Times.Once);
        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_HeaderExcede64Caracteres_PasaNullAlResolver()
    {
        // Arrange — crear un header de más de 64 caracteres
        var headerLargo = new string('A', 65);
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(CreateUser(1, 10));
        context.Request.Headers["X-Timezone"] = headerLargo;

        _resolverMock
            .Setup(r => r.ResolveAsync(1, 10, null))
            .ReturnsAsync("UTC");

        // Act
        await middleware.InvokeAsync(context);

        // Assert — el middleware debe pasar null (ignora headers largos)
        _resolverMock.Verify(r => r.ResolveAsync(1, 10, null), Times.Once);
        Assert.True(_nextCalled);
    }

    // ── Almacenamiento en HttpContext.Items ──────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ResolverRetornaTimezone_AlmacenaEnContextItems()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(CreateUser(1, 10));
        context.Request.Headers["X-Timezone"] = "America/Guayaquil";

        _resolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync("America/Guayaquil");

        // Act
        await middleware.InvokeAsync(context);

        // Assert — el resultado se almacena en HttpContext.Items
        Assert.Equal("America/Guayaquil", context.Items["ResolvedTimezone"]);
    }

    // ── Fallback a UTC cuando hay excepción ─────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ResolverLanzaExcepcion_AlmacenaUTCComoFallback()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(CreateUser(1, 10));

        _resolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("Error de prueba"));

        // Act
        await middleware.InvokeAsync(context);

        // Assert — debe usar UTC como fallback y continuar con el pipeline
        Assert.Equal("UTC", context.Items["ResolvedTimezone"]);
        Assert.True(_nextCalled);
    }

    // ── Header ausente o vacío ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_SinHeaderXTimezone_PasaNullAlResolver()
    {
        // Arrange — no se agrega el header X-Timezone
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(CreateUser(1, 10));

        _resolverMock
            .Setup(r => r.ResolveAsync(1, 10, null))
            .ReturnsAsync("UTC");

        // Act
        await middleware.InvokeAsync(context);

        // Assert — sin header, el valor debe ser null
        _resolverMock.Verify(r => r.ResolveAsync(1, 10, null), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task InvokeAsync_HeaderVacioOEspacios_PasaNullAlResolver(string headerValue)
    {
        // Arrange — header con valor vacío o solo espacios
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(CreateUser(1, 10));
        context.Request.Headers["X-Timezone"] = headerValue;

        _resolverMock
            .Setup(r => r.ResolveAsync(1, 10, null))
            .ReturnsAsync("UTC");

        // Act
        await middleware.InvokeAsync(context);

        // Assert — whitespace se trata como null
        _resolverMock.Verify(r => r.ResolveAsync(1, 10, null), Times.Once);
    }
}
