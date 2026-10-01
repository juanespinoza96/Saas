using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Api.Filters;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Tests.Filters;

/// <summary>
/// Tests unitarios para TimezoneResponseFilter.
/// Valida que el filtro agrega el header X-Applied-Timezone,
/// omite conversión cuando timezone es UTC, y maneja errores correctamente.
/// </summary>
public class TimezoneResponseFilterUnitTests
{
    private readonly Mock<ITimezoneService> _timezoneMock;
    private readonly Mock<ILogger<TimezoneResponseFilter>> _loggerMock;
    private readonly TimezoneResponseFilter _filter;

    public TimezoneResponseFilterUnitTests()
    {
        _timezoneMock = new Mock<ITimezoneService>();
        _loggerMock = new Mock<ILogger<TimezoneResponseFilter>>();
        _filter = new TimezoneResponseFilter(_timezoneMock.Object, _loggerMock.Object);
    }

    #region Helpers

    /// <summary>
    /// DTO simple para pruebas con un campo DateTime.
    /// </summary>
    private class SimpleDto
    {
        public DateTime FechaCreacion { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    /// <summary>
    /// Crea un ResultExecutingContext con el objeto de respuesta y timezone dados.
    /// </summary>
    private static ResultExecutingContext CreateContext(object responseObject, string timezone)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["ResolvedTimezone"] = timezone;

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());

        var objectResult = new ObjectResult(responseObject);

        return new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            objectResult,
            controller: null!);
    }

    /// <summary>
    /// Ejecuta el filtro con el contexto proporcionado.
    /// </summary>
    private async Task ExecuteFilter(ResultExecutingContext context)
    {
        ResultExecutionDelegate next = () => Task.FromResult(
            new ResultExecutedContext(
                new ActionContext(
                    context.HttpContext,
                    context.HttpContext.GetRouteData(),
                    context.ActionDescriptor,
                    context.ModelState),
                context.Filters.ToList(),
                context.Result,
                controller: null!));

        await _filter.OnResultExecutionAsync(context, next);
    }

    /// <summary>
    /// IEnumerable que lanza excepción durante la iteración.
    /// Se usa como Value directo del ObjectResult para activar el catch externo.
    /// </summary>
    private static IEnumerable<object> ColeccionQueExplota()
    {
        yield return new object();
        throw new InvalidOperationException("Error catastrófico al enumerar");
    }

    #endregion

    // ── Header X-Applied-Timezone se agrega con el valor resuelto ────────────

    [Fact]
    public async Task OnResultExecutionAsync_TimezoneNoUtc_AgregaHeaderConTimezoneAplicada()
    {
        // Arrange
        var dto = new SimpleDto
        {
            FechaCreacion = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            Nombre = "Test"
        };
        var context = CreateContext(dto, "America/Guayaquil");

        _timezoneMock
            .Setup(s => s.ConvertFromUtc(It.IsAny<DateTime>(), "America/Guayaquil"))
            .Returns((DateTime dt, string _) => dt.AddHours(-5));

        // Act
        await ExecuteFilter(context);

        // Assert: el header debe contener la timezone aplicada
        Assert.Equal("America/Guayaquil", context.HttpContext.Response.Headers["X-Applied-Timezone"].ToString());
    }

    // ── No invoca ConvertFromUtc cuando timezone es UTC ──────────────────────

    [Fact]
    public async Task OnResultExecutionAsync_TimezoneEsUTC_NoLlamaConvertFromUtc()
    {
        // Arrange
        var dto = new SimpleDto
        {
            FechaCreacion = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            Nombre = "Test"
        };
        var context = CreateContext(dto, "UTC");

        // Act
        await ExecuteFilter(context);

        // Assert: ConvertFromUtc no debe ser invocado
        _timezoneMock.Verify(
            s => s.ConvertFromUtc(It.IsAny<DateTime>(), It.IsAny<string>()),
            Times.Never);
    }

    // ── Header X-Applied-Timezone: UTC cuando timezone es UTC ────────────────

    [Fact]
    public async Task OnResultExecutionAsync_TimezoneEsUTC_AgregaHeaderUTC()
    {
        // Arrange
        var dto = new SimpleDto
        {
            FechaCreacion = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            Nombre = "Test"
        };
        var context = CreateContext(dto, "UTC");

        // Act
        await ExecuteFilter(context);

        // Assert: el header indica UTC
        Assert.Equal("UTC", context.HttpContext.Response.Headers["X-Applied-Timezone"].ToString());
    }

    // ── Fail-safe: header UTC cuando la conversión lanza excepción ───────────

    [Fact]
    public async Task OnResultExecutionAsync_ConversionLanzaExcepcion_AgregaHeaderUTC()
    {
        // Arrange: usar directamente un IEnumerable como Value del ObjectResult.
        // Cuando el filtro detecta que el Value es IEnumerable, intenta iterar con foreach.
        // La excepción lazy escapa del foreach (fuera del try-catch por propiedad)
        // y es capturada por el catch externo del filtro (fail-safe: UTC).
        var listaExplosiva = ColeccionQueExplota();

        var httpContext = new DefaultHttpContext();
        httpContext.Items["ResolvedTimezone"] = "America/Guayaquil";

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());

        var objectResult = new ObjectResult(listaExplosiva);

        var executingContext = new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            objectResult,
            controller: null!);

        ResultExecutionDelegate next = () => Task.FromResult(
            new ResultExecutedContext(
                actionContext,
                new List<IFilterMetadata>(),
                objectResult,
                controller: null!));

        // Act
        await _filter.OnResultExecutionAsync(executingContext, next);

        // Assert: fail-safe retorna UTC cuando la conversión falla
        Assert.Equal("UTC", httpContext.Response.Headers["X-Applied-Timezone"].ToString());
    }

    // ── Convierte campos DateTime cuando timezone es válida y no-UTC ─────────

    [Fact]
    public async Task OnResultExecutionAsync_TimezoneValida_ConvierteCamposDateTime()
    {
        // Arrange
        var fechaOriginal = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var fechaConvertida = new DateTime(2024, 6, 15, 7, 0, 0, DateTimeKind.Unspecified);
        var dto = new SimpleDto { FechaCreacion = fechaOriginal, Nombre = "Test" };
        var context = CreateContext(dto, "America/Guayaquil");

        _timezoneMock
            .Setup(s => s.ConvertFromUtc(fechaOriginal, "America/Guayaquil"))
            .Returns(fechaConvertida);

        // Act
        await ExecuteFilter(context);

        // Assert: el campo DateTime fue convertido
        Assert.Equal(fechaConvertida, dto.FechaCreacion);
        _timezoneMock.Verify(s => s.ConvertFromUtc(fechaOriginal, "America/Guayaquil"), Times.Once);
    }
}
