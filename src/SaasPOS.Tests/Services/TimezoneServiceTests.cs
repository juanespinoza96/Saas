using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Tests unitarios para el servicio centralizado de conversión de zona horaria.
/// Valida: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.7
/// </summary>
public class TimezoneServiceTests
{
    private readonly TimezoneService _sut;
    private readonly Mock<ILogger<TimezoneService>> _loggerMock;

    public TimezoneServiceTests()
    {
        _loggerMock = new Mock<ILogger<TimezoneService>>();
        _sut = new TimezoneService(_loggerMock.Object);
    }

    #region ConvertFromUtc - Conversiones correctas

    /// <summary>
    /// Verifica que ConvertFromUtc convierte correctamente UTC a America/Guayaquil (UTC-5).
    /// 15:00 UTC = 10:00 ECT
    /// </summary>
    [Fact]
    public void ConvertFromUtc_ConvierteCorrectamente_GuayaquilUTC()
    {
        // Arrange
        var utcDateTime = new DateTime(2024, 1, 15, 15, 0, 0, DateTimeKind.Utc);
        var zonaHoraria = "America/Guayaquil";

        // Act
        var resultado = _sut.ConvertFromUtc(utcDateTime, zonaHoraria);

        // Assert
        var esperado = new DateTime(2024, 1, 15, 10, 0, 0);
        Assert.Equal(esperado, resultado);
    }

    #endregion

    #region ConvertToUtc - Conversiones correctas

    /// <summary>
    /// Verifica que ConvertToUtc convierte correctamente America/Guayaquil a UTC.
    /// 10:00 ECT = 15:00 UTC
    /// </summary>
    [Fact]
    public void ConvertToUtc_ConvierteCorrectamente_GuayaquilUTC()
    {
        // Arrange
        var localDateTime = new DateTime(2024, 1, 15, 10, 0, 0);
        var zonaHoraria = "America/Guayaquil";

        // Act
        var resultado = _sut.ConvertToUtc(localDateTime, zonaHoraria);

        // Assert
        var esperado = new DateTime(2024, 1, 15, 15, 0, 0);
        Assert.Equal(esperado, resultado);
    }

    #endregion

    #region Spring-forward (DST)

    /// <summary>
    /// Verifica el comportamiento durante spring-forward en America/New_York.
    /// En 2024, spring-forward ocurre el 10 de marzo a las 2:00 AM (saltan a 3:00 AM).
    /// 06:59 UTC = 01:59 EST (antes del cambio).
    /// 07:01 UTC = 03:01 EDT (después del cambio).
    /// </summary>
    [Fact]
    public void ConvertFromUtc_SpringForward_AjustaAlInstantePosterior()
    {
        // Arrange - antes del spring-forward: 06:59 UTC = 01:59 AM EST
        var utcAntesDelCambio = new DateTime(2024, 3, 10, 6, 59, 0, DateTimeKind.Utc);
        var zonaHoraria = "America/New_York";

        // Act
        var resultadoAntes = _sut.ConvertFromUtc(utcAntesDelCambio, zonaHoraria);

        // Assert - debe ser 01:59 AM (todavía EST, UTC-5)
        var esperadoAntes = new DateTime(2024, 3, 10, 1, 59, 0);
        Assert.Equal(esperadoAntes, resultadoAntes);

        // Arrange - después del spring-forward: 07:01 UTC = 03:01 AM EDT
        var utcDespuesDelCambio = new DateTime(2024, 3, 10, 7, 1, 0, DateTimeKind.Utc);

        // Act
        var resultadoDespues = _sut.ConvertFromUtc(utcDespuesDelCambio, zonaHoraria);

        // Assert - debe ser 03:01 AM (ya en EDT, UTC-4)
        var esperadoDespues = new DateTime(2024, 3, 10, 3, 1, 0);
        Assert.Equal(esperadoDespues, resultadoDespues);
    }

    #endregion

    #region Fall-back (DST)

    /// <summary>
    /// Verifica que ConvertToUtc resuelve ambigüedad de fall-back usando offset estándar.
    /// En 2024, fall-back ocurre el 3 de noviembre a las 2:00 AM en America/New_York.
    /// 01:30 AM es ambiguo (puede ser EDT o EST).
    /// Nuestra implementación asume offset estándar (EST, UTC-5).
    /// Esperado: 01:30 + 5 horas = 06:30 UTC.
    /// </summary>
    [Fact]
    public void ConvertToUtc_FallBack_AssumeOffsetEstandar()
    {
        // Arrange
        var localAmbiguo = new DateTime(2024, 11, 3, 1, 30, 0);
        var zonaHoraria = "America/New_York";

        // Act
        var resultado = _sut.ConvertToUtc(localAmbiguo, zonaHoraria);

        // Assert - con offset estándar (EST = UTC-5): 01:30 + 5 = 06:30 UTC
        var esperado = new DateTime(2024, 11, 3, 6, 30, 0);
        Assert.Equal(esperado, resultado);
    }

    #endregion

    #region Zona inválida - Retorna valor sin modificar

    /// <summary>
    /// Verifica que ConvertFromUtc retorna el valor sin modificar cuando la zona es inválida.
    /// </summary>
    [Fact]
    public void ConvertFromUtc_ZonaInvalida_RetornaValorSinModificar()
    {
        // Arrange
        var utcDateTime = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        var zonaInvalida = "Invalid/Timezone";

        // Act
        var resultado = _sut.ConvertFromUtc(utcDateTime, zonaInvalida);

        // Assert - el valor debe ser exactamente el mismo
        Assert.Equal(utcDateTime, resultado);
    }

    /// <summary>
    /// Verifica que ConvertToUtc retorna el valor sin modificar cuando la zona es inválida.
    /// </summary>
    [Fact]
    public void ConvertToUtc_ZonaInvalida_RetornaValorSinModificar()
    {
        // Arrange
        var localDateTime = new DateTime(2024, 6, 15, 12, 30, 0);
        var zonaInvalida = "Invalid/Timezone";

        // Act
        var resultado = _sut.ConvertToUtc(localDateTime, zonaInvalida);

        // Assert - el valor debe ser exactamente el mismo
        Assert.Equal(localDateTime, resultado);
    }

    #endregion

    #region DateTime default - Lanza ArgumentNullException

    /// <summary>
    /// Verifica que ConvertFromUtc lanza ArgumentNullException cuando el DateTime es default.
    /// </summary>
    [Fact]
    public void ConvertFromUtc_DateTimeDefault_LanzaArgumentNullException()
    {
        // Arrange
        var fechaInvalida = default(DateTime);
        var zonaHoraria = "America/Guayaquil";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _sut.ConvertFromUtc(fechaInvalida, zonaHoraria));
    }

    /// <summary>
    /// Verifica que ConvertToUtc lanza ArgumentNullException cuando el DateTime es default.
    /// </summary>
    [Fact]
    public void ConvertToUtc_DateTimeDefault_LanzaArgumentNullException()
    {
        // Arrange
        var fechaInvalida = default(DateTime);
        var zonaHoraria = "America/Guayaquil";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _sut.ConvertToUtc(fechaInvalida, zonaHoraria));
    }

    #endregion

    #region IsValidTimeZone

    /// <summary>
    /// Verifica que IsValidTimeZone retorna true para una zona válida.
    /// </summary>
    [Fact]
    public void IsValidTimeZone_ZonaValida_RetornaTrue()
    {
        // Arrange
        var zonaValida = "America/Guayaquil";

        // Act
        var resultado = _sut.IsValidTimeZone(zonaValida);

        // Assert
        Assert.True(resultado);
    }

    /// <summary>
    /// Verifica que IsValidTimeZone retorna false para una zona inválida.
    /// </summary>
    [Fact]
    public void IsValidTimeZone_ZonaInvalida_RetornaFalse()
    {
        // Arrange
        var zonaInvalida = "Invalid/Zone";

        // Act
        var resultado = _sut.IsValidTimeZone(zonaInvalida);

        // Assert
        Assert.False(resultado);
    }

    /// <summary>
    /// Verifica que IsValidTimeZone retorna false para un string vacío.
    /// </summary>
    [Fact]
    public void IsValidTimeZone_StringVacio_RetornaFalse()
    {
        // Arrange
        var zonaVacia = "";

        // Act
        var resultado = _sut.IsValidTimeZone(zonaVacia);

        // Assert
        Assert.False(resultado);
    }

    #endregion

    #region GetAvailableTimezones

    /// <summary>
    /// Verifica que GetAvailableTimezones retorna una lista no vacía con zonas conocidas.
    /// </summary>
    [Fact]
    public void GetAvailableTimezones_RetornaListaNoVacia()
    {
        // Act
        var resultado = _sut.GetAvailableTimezones();

        // Assert - la lista no debe estar vacía
        Assert.NotEmpty(resultado);

        // Debe contener zonas conocidas como America/Guayaquil o UTC
        var contieneZonaConocida = resultado.Any(tz =>
            tz.Id == "America/Guayaquil" || tz.Id == "UTC");
        Assert.True(contieneZonaConocida,
            "La lista debe contener al menos una zona conocida (America/Guayaquil o UTC).");
    }

    #endregion
}
