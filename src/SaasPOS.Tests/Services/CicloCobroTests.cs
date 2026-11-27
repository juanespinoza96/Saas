using SaasPOS.Application.Helpers;
using Xunit;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Ejemplos del ADR 002 para el cálculo del ciclo de cobro (cuota proporcional y próximo corte).
/// </summary>
public class CicloCobroTests
{
    // Inicio el 25-nov: noviembre tiene 30 días y faltan 8 hasta el corte del 3-dic.
    [Theory]
    [InlineData(50, "13.33")]
    [InlineData(200, "53.33")]
    [InlineData(420, "112.00")]
    public void Calcular_Inicio25Noviembre_CuotaProporcionalYCorte3Diciembre(int mensualidad, string cuotaEsperada)
    {
        var resultado = CicloCobro.Calcular(mensualidad, new DateOnly(2025, 11, 25));

        Assert.Equal(decimal.Parse(cuotaEsperada, System.Globalization.CultureInfo.InvariantCulture), resultado.Cuota);
        Assert.Equal(new DateOnly(2025, 12, 3), resultado.ProximoCorte);
        Assert.True(resultado.EsProporcional);
    }

    // Inicio en día 3: cuota completa, no proporcional, corte el día 3 del mes siguiente.
    [Fact]
    public void Calcular_InicioDia3_CobraMensualidadCompleta()
    {
        var resultado = CicloCobro.Calcular(200m, new DateOnly(2025, 11, 3));

        Assert.Equal(200m, resultado.Cuota);
        Assert.Equal(new DateOnly(2025, 12, 3), resultado.ProximoCorte);
        Assert.False(resultado.EsProporcional);
    }

    // Día 1 de enero (31 días): faltan 2 días hasta el corte del 3-ene.
    [Fact]
    public void Calcular_InicioDia1_CobraDosDiasYCorteMismoMes()
    {
        var resultado = CicloCobro.Calcular(310m, new DateOnly(2025, 1, 1));

        Assert.Equal(20.00m, resultado.Cuota);
        Assert.Equal(new DateOnly(2025, 1, 3), resultado.ProximoCorte);
        Assert.True(resultado.EsProporcional);
    }

    // Día 2 de enero: falta 1 día hasta el corte del 3-ene.
    [Fact]
    public void Calcular_InicioDia2_CobraUnDiaYCorteMismoMes()
    {
        var resultado = CicloCobro.Calcular(310m, new DateOnly(2025, 1, 2));

        Assert.Equal(10.00m, resultado.Cuota);
        Assert.Equal(new DateOnly(2025, 1, 3), resultado.ProximoCorte);
        Assert.True(resultado.EsProporcional);
    }

    // Fin de mes (31-ene): 3 días hasta el 3-feb.
    [Fact]
    public void Calcular_FinDeMes31Enero_CorteEnFebrero()
    {
        var resultado = CicloCobro.Calcular(310m, new DateOnly(2025, 1, 31));

        Assert.Equal(30.00m, resultado.Cuota);
        Assert.Equal(new DateOnly(2025, 2, 3), resultado.ProximoCorte);
    }

    // Fin de año (31-dic): el corte pasa al 3-ene del año siguiente.
    [Fact]
    public void Calcular_31Diciembre_CorteEnEneroSiguienteAnio()
    {
        var resultado = CicloCobro.Calcular(310m, new DateOnly(2025, 12, 31));

        Assert.Equal(30.00m, resultado.Cuota);
        Assert.Equal(new DateOnly(2026, 1, 3), resultado.ProximoCorte);
    }

    // Año bisiesto: febrero 2024 tiene 29 días; del 28-feb al 3-mar hay 4 días.
    [Fact]
    public void Calcular_AnioBisiesto28Febrero_UsaVeintinueveDiasDelMes()
    {
        var resultado = CicloCobro.Calcular(290m, new DateOnly(2024, 2, 28));

        Assert.Equal(40.00m, resultado.Cuota);
        Assert.Equal(new DateOnly(2024, 3, 3), resultado.ProximoCorte);
    }

    // Año no bisiesto: febrero 2023 tiene 28 días; del 28-feb al 3-mar hay 3 días.
    [Fact]
    public void Calcular_AnioNoBisiesto28Febrero_UsaVeintiochoDiasDelMes()
    {
        var resultado = CicloCobro.Calcular(280m, new DateOnly(2023, 2, 28));

        Assert.Equal(30.00m, resultado.Cuota);
        Assert.Equal(new DateOnly(2023, 3, 3), resultado.ProximoCorte);
    }
}
