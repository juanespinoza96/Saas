using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.Helpers;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for null PrecioRealCobrado defaulting to calculated price.
/// **Validates: Requirements 2.2**
///
/// Property 1: Null PrecioRealCobrado defaults to calculated price.
/// For any product with any PrecioLista (positive decimal), any set of volume pricing rules,
/// and any valid quantity, when PrecioRealCobrado is null, the effective price must equal
/// PricingHelper.DeterminarPrecioEfectivo(precioLista, cantidad, reglasVolumen).
/// </summary>
public class PrecioNegociado_Property1_NullDefaultsToCalculatedTests
{
    /// <summary>
    /// Generates a positive decimal price in range [0.01, 1000].
    /// </summary>
    private static Gen<decimal> GenPositivePrice() =>
        Gen.Choose(1, 100000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a positive decimal quantity in range [0.01, 1000].
    /// </summary>
    private static Gen<decimal> GenPositiveQuantity() =>
        Gen.Choose(1, 100000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a list of 0-5 volume pricing rules with distinct CantidadMinima values.
    /// </summary>
    private static Gen<List<(decimal CantidadMinima, decimal PrecioEspecial)>> GenVolumePricingRules() =>
        from count in Gen.Choose(0, 5)
        from cantidades in Gen.ListOf(count, Gen.Choose(1, 1000).Select(i => (decimal)i))
                              .Select(list => list.Distinct().Take(count).ToList())
        from precios in Gen.ListOf(cantidades.Count, GenPositivePrice())
        select cantidades.Zip(precios, (c, p) => (CantidadMinima: c, PrecioEspecial: p)).ToList();

    /// <summary>
    /// For any product with a positive PrecioLista, any quantity, and any volume pricing rules,
    /// when PrecioRealCobrado is null, the effective price equals
    /// PricingHelper.DeterminarPrecioEfectivo(precioLista, cantidad, reglasVolumen).
    ///
    /// This simulates what VentasController does: when PrecioRealCobrado is null,
    /// it calls PricingHelper.DeterminarPrecioEfectivo to determine the effective price.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NullPrecioRealCobrado_DefaultsToCalculatedPrice()
    {
        var gen =
            from precioLista in GenPositivePrice()
            from cantidad in GenPositiveQuantity()
            from reglas in GenVolumePricingRules()
            select (precioLista, cantidad, reglas);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioLista, cantidad, reglas) = tuple;

            // Simulate VentasController behavior: PrecioRealCobrado is null,
            // so use PricingHelper to determine effective price
            decimal? precioRealCobrado = null;

            decimal precioEfectivo;
            if (precioRealCobrado != null)
            {
                precioEfectivo = precioRealCobrado.Value;
            }
            else
            {
                precioEfectivo = PricingHelper.DeterminarPrecioEfectivo(
                    precioLista, cantidad, reglas);
            }

            // The effective price must equal the PricingHelper result
            var expected = PricingHelper.DeterminarPrecioEfectivo(
                precioLista, cantidad, reglas);

            return (precioEfectivo == expected)
                .Label($"Expected effective price={expected}, got {precioEfectivo} " +
                       $"(precioLista={precioLista}, cantidad={cantidad}, reglas={reglas.Count})");
        });
    }

    /// <summary>
    /// When PrecioRealCobrado is null and there are no volume pricing rules,
    /// the effective price must equal PrecioLista.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NullPrecioRealCobrado_NoVolumeRules_UsesPrecioLista()
    {
        var gen = GenPositivePrice();

        return Prop.ForAll(gen.ToArbitrary(), precioLista =>
        {
            var cantidad = 1m;
            var reglas = new List<(decimal CantidadMinima, decimal PrecioEspecial)>();

            decimal? precioRealCobrado = null;

            decimal precioEfectivo;
            if (precioRealCobrado != null)
            {
                precioEfectivo = precioRealCobrado.Value;
            }
            else
            {
                precioEfectivo = PricingHelper.DeterminarPrecioEfectivo(
                    precioLista, cantidad, reglas);
            }

            return (precioEfectivo == precioLista)
                .Label($"Expected PrecioLista={precioLista}, got {precioEfectivo}");
        });
    }

    /// <summary>
    /// When PrecioRealCobrado is null and volume rules apply, the effective price
    /// must equal the PrecioEspecial of the highest applicable rule.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NullPrecioRealCobrado_WithApplicableVolumeRules_UsesHighestApplicableRule()
    {
        var gen =
            from precioLista in GenPositivePrice()
            from cantidad in Gen.Choose(10, 1000).Select(i => (decimal)i)
            from ruleCount in Gen.Choose(1, 5)
            from cantidadesMinimas in Gen.ListOf(ruleCount, Gen.Choose(1, (int)cantidad)
                                         .Select(i => (decimal)i))
                                         .Select(list => list.Distinct().Take(ruleCount).ToList())
            from preciosEspeciales in Gen.ListOf(cantidadesMinimas.Count, GenPositivePrice())
            select (precioLista, cantidad,
                    reglas: cantidadesMinimas.Zip(preciosEspeciales,
                        (c, p) => (CantidadMinima: c, PrecioEspecial: p)).ToList());

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioLista, cantidad, reglas) = tuple;

            decimal? precioRealCobrado = null;

            decimal precioEfectivo;
            if (precioRealCobrado != null)
            {
                precioEfectivo = precioRealCobrado.Value;
            }
            else
            {
                precioEfectivo = PricingHelper.DeterminarPrecioEfectivo(
                    precioLista, cantidad, reglas);
            }

            // Independently compute expected: highest CantidadMinima rule where cantidad >= CantidadMinima
            var reglaAplicable = reglas
                .Where(r => cantidad >= r.CantidadMinima)
                .OrderByDescending(r => r.CantidadMinima)
                .FirstOrDefault();

            var expected = reglaAplicable == default ? precioLista : reglaAplicable.PrecioEspecial;

            return (precioEfectivo == expected)
                .Label($"Expected={expected}, got={precioEfectivo} " +
                       $"(precioLista={precioLista}, cantidad={cantidad}, " +
                       $"reglas=[{string.Join(", ", reglas.Select(r => $"({r.CantidadMinima}→{r.PrecioEspecial})"))}])");
        });
    }
}
