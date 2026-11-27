namespace SaasPOS.Application.Helpers;

/// <summary>
/// Resultado del cálculo de un ciclo de cobro: cuota a cobrar, próximo corte y si la cuota es proporcional.
/// </summary>
public sealed record CalculoCiclo(decimal Cuota, DateOnly ProximoCorte, bool EsProporcional);

/// <summary>
/// Lógica pura del ciclo de cobro mensual. El corte ocurre siempre el día 3 de cada mes.
/// </summary>
public static class CicloCobro
{
    /// <summary>Día del mes en que se realiza el corte de cobro.</summary>
    public const int DiaCorte = 3;

    /// <summary>
    /// Devuelve el primer día 3 estrictamente posterior a la fecha indicada.
    /// </summary>
    public static DateOnly ProximoCorteDesde(DateOnly fecha)
    {
        // Antes del día 3: el corte es en el mes actual; desde el día 3: en el mes siguiente.
        var baseMes = fecha.Day < DiaCorte
            ? new DateOnly(fecha.Year, fecha.Month, 1)
            : new DateOnly(fecha.Year, fecha.Month, 1).AddMonths(1);
        return new DateOnly(baseMes.Year, baseMes.Month, DiaCorte);
    }

    /// <summary>
    /// Calcula la cuota del primer ciclo. Si la fecha es día 3 cobra la mensualidad completa;
    /// si no, (mensualidad / días del mes de la fecha) x días hasta el próximo día 3,
    /// redondeada a 2 decimales con el modo de redondeo por defecto de Math.Round.
    /// </summary>
    public static decimal CuotaProporcional(decimal mensualidad, DateOnly inicio)
    {
        if (inicio.Day == DiaCorte)
            return mensualidad;

        var diasTotalesMes = DateTime.DaysInMonth(inicio.Year, inicio.Month);
        var diasRestantes = ProximoCorteDesde(inicio).DayNumber - inicio.DayNumber;

        var cuota = (mensualidad / diasTotalesMes) * diasRestantes;
        return Math.Round(cuota, 2);
    }

    /// <summary>
    /// Calcula cuota, próximo corte e indicador de proporcionalidad para una fecha de inicio.
    /// </summary>
    public static CalculoCiclo Calcular(decimal mensualidad, DateOnly inicio)
    {
        return new CalculoCiclo(
            CuotaProporcional(mensualidad, inicio),
            ProximoCorteDesde(inicio),
            inicio.Day != DiaCorte);
    }
}
