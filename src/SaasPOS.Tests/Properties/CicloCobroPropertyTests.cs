using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.Helpers;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Pruebas de propiedades del helper de ciclo de cobro (<see cref="CicloCobro"/>).
/// **Validates: Requirements 3.1, 4.2, 6.2, 1.3, 3.2, 4.3**
/// </summary>
public class CicloCobroPropertyTests
{
    /// <summary>Genera una fecha entre 2020 y 2035 (incluye años bisiestos y fines de mes).</summary>
    private static Gen<DateOnly> GenFecha() =>
        Gen.Choose(0, (new DateOnly(2035, 12, 31).DayNumber - new DateOnly(2020, 1, 1).DayNumber))
           .Select(n => DateOnly.FromDayNumber(new DateOnly(2020, 1, 1).DayNumber + n));

    /// <summary>Genera una mensualidad positiva entre 0.01 y 9999.99.</summary>
    private static Gen<decimal> GenMensualidad() =>
        Gen.Choose(1, 999999).Select(i => (decimal)i / 100m);

    /// <summary>
    /// El próximo corte siempre es un día 3, posterior a la fecha y a no más de 31 días.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ProximoCorte_EsDia3_PosteriorYAMenosDe31Dias()
    {
        return Prop.ForAll(Arb.From(GenFecha()), fecha =>
        {
            var corte = CicloCobro.ProximoCorteDesde(fecha);
            var dias = corte.DayNumber - fecha.DayNumber;
            return (corte.Day == CicloCobro.DiaCorte && dias >= 1 && dias <= 31)
                .Label($"fecha={fecha}, corte={corte}, dias={dias}");
        });
    }

    /// <summary>
    /// La cuota está entre 0 y la mensualidad, y es exactamente la mensualidad si la fecha es día 3.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Cuota_EntreCeroYMensualidad_ExactaEnDia3()
    {
        return Prop.ForAll(Arb.From(GenMensualidad()), Arb.From(GenFecha()), (mensualidad, fecha) =>
        {
            var cuota = CicloCobro.CuotaProporcional(mensualidad, fecha);
            var enRango = cuota >= 0m && cuota <= mensualidad;
            var exactaDia3 = fecha.Day != CicloCobro.DiaCorte || cuota == mensualidad;
            return (enRango && exactaDia3)
                .Label($"mensualidad={mensualidad}, fecha={fecha}, cuota={cuota}");
        });
    }

    /// <summary>
    /// Calcular es coherente con las funciones individuales: cuota, corte y proporcionalidad.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Calcular_EsCoherenteConFuncionesIndividuales()
    {
        return Prop.ForAll(Arb.From(GenMensualidad()), Arb.From(GenFecha()), (mensualidad, fecha) =>
        {
            var r = CicloCobro.Calcular(mensualidad, fecha);
            return (r.Cuota == CicloCobro.CuotaProporcional(mensualidad, fecha)
                    && r.ProximoCorte == CicloCobro.ProximoCorteDesde(fecha)
                    && r.EsProporcional == (fecha.Day != CicloCobro.DiaCorte))
                .Label($"mensualidad={mensualidad}, fecha={fecha}");
        });
    }
}
