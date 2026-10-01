using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.Helpers;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for volume pricing tier selection logic.
/// **Validates: Requirements 8.2, 8.3**
/// 
/// Property 11: Volume pricing selects the highest applicable tier.
/// For any product with a set of PreciosVolumen rules and any sale quantity Q,
/// the effective price SHALL be the PrecioEspecial of the rule with the highest
/// CantidadMinima that is less than or equal to Q. If no rule applies, PrecioLista is used.
/// </summary>
public class Property11_VolumePricingTierSelectionTests
{
    /// <summary>
    /// Generates a positive decimal value in a reasonable range for prices/quantities.
    /// </summary>
    private static Arbitrary<decimal> PositiveDecimalArb() =>
        Arb.From(
            Gen.Choose(1, 100000)
               .Select(i => (decimal)i / 100m));

    /// <summary>
    /// Generates a list of volume pricing rules with distinct CantidadMinima values.
    /// Each rule has CantidadMinima > 0 and PrecioEspecial > 0.
    /// </summary>
    private static Gen<List<(decimal CantidadMinima, decimal PrecioEspecial)>> GenDistinctRules(int minCount, int maxCount) =>
        from count in Gen.Choose(minCount, maxCount)
        from cantidades in Gen.ListOf(count, Gen.Choose(1, 10000).Select(i => (decimal)i))
                              .Select(list => list.Distinct().Take(count).ToList())
        from precios in Gen.ListOf(cantidades.Count, Gen.Choose(1, 100000).Select(i => (decimal)i / 100m))
        select cantidades.Zip(precios, (c, p) => (CantidadMinima: c, PrecioEspecial: p)).ToList();

    /// <summary>
    /// When quantity is below ALL CantidadMinima values in the volume rules,
    /// PrecioLista must always be returned.
    /// **Validates: Requirements 8.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property WhenQuantityBelowAllRules_PrecioListaIsApplied()
    {
        var gen =
            from precioLista in PositiveDecimalArb().Generator
            from rules in GenDistinctRules(1, 10)
            let minCantidad = rules.Min(r => r.CantidadMinima)
            from cantidad in Gen.Choose(1, (int)(minCantidad * 100) - 1)
                                .Select(i => (decimal)i / 100m)
                                .Where(q => q < minCantidad)
            select (precioLista, cantidad, rules);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioLista, cantidad, rules) = tuple;

            var result = PricingHelper.DeterminarPrecioEfectivo(precioLista, cantidad, rules);

            return (result == precioLista)
                .Label($"Expected PrecioLista={precioLista}, got {result} for cantidad={cantidad}, min rule={rules.Min(r => r.CantidadMinima)}");
        });
    }

    /// <summary>
    /// When quantity meets or exceeds at least one rule's CantidadMinima,
    /// the rule with the HIGHEST CantidadMinima that quantity still satisfies is selected.
    /// **Validates: Requirements 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property WhenQuantityMeetsRule_HighestApplicableTierSelected()
    {
        var gen =
            from precioLista in PositiveDecimalArb().Generator
            from rules in GenDistinctRules(1, 10)
            let maxCantidad = rules.Max(r => r.CantidadMinima)
            from cantidad in Gen.Choose((int)rules.Min(r => r.CantidadMinima), (int)maxCantidad + 100)
                                .Select(i => (decimal)i)
                                .Where(q => rules.Any(r => q >= r.CantidadMinima))
            select (precioLista, cantidad, rules);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioLista, cantidad, rules) = tuple;

            var result = PricingHelper.DeterminarPrecioEfectivo(precioLista, cantidad, rules);

            // Expected: the PrecioEspecial of the rule with the highest CantidadMinima <= cantidad
            var expectedRule = rules
                .Where(r => cantidad >= r.CantidadMinima)
                .OrderByDescending(r => r.CantidadMinima)
                .First();

            return (result == expectedRule.PrecioEspecial)
                .Label($"Expected PrecioEspecial={expectedRule.PrecioEspecial} (tier CantidadMinima={expectedRule.CantidadMinima}), got {result} for cantidad={cantidad}");
        });
    }

    /// <summary>
    /// For any single rule, when quantity is exactly equal to CantidadMinima,
    /// that rule's PrecioEspecial must be selected.
    /// **Validates: Requirements 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property WhenQuantityExactlyEqualsToCantidadMinima_RuleIsSelected()
    {
        var gen =
            from precioLista in PositiveDecimalArb().Generator
            from cantidadMinima in Gen.Choose(1, 10000).Select(i => (decimal)i)
            from precioEspecial in PositiveDecimalArb().Generator
            select (precioLista, cantidadMinima, precioEspecial);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioLista, cantidadMinima, precioEspecial) = tuple;

            var rules = new List<(decimal CantidadMinima, decimal PrecioEspecial)>
            {
                (cantidadMinima, precioEspecial)
            };

            // Quantity exactly equals the rule's CantidadMinima
            var result = PricingHelper.DeterminarPrecioEfectivo(precioLista, cantidadMinima, rules);

            return (result == precioEspecial)
                .Label($"Expected PrecioEspecial={precioEspecial} when cantidad={cantidadMinima} exactly equals CantidadMinima, got {result}");
        });
    }

    /// <summary>
    /// With no volume rules, PrecioLista is always returned regardless of quantity.
    /// **Validates: Requirements 8.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property WhenNoRulesExist_PrecioListaAlwaysReturned()
    {
        var gen =
            from precioLista in PositiveDecimalArb().Generator
            from cantidad in PositiveDecimalArb().Generator
            select (precioLista, cantidad);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioLista, cantidad) = tuple;

            var emptyRules = Enumerable.Empty<(decimal CantidadMinima, decimal PrecioEspecial)>();
            var result = PricingHelper.DeterminarPrecioEfectivo(precioLista, cantidad, emptyRules);

            return (result == precioLista)
                .Label($"Expected PrecioLista={precioLista} with no rules, got {result}");
        });
    }

    /// <summary>
    /// Concrete example: Given ordered rules [10→$8, 50→$6, 100→$5], 
    /// quantity=75 should select the 50→$6 rule (highest CantidadMinima <= 75).
    /// **Validates: Requirements 8.2**
    /// </summary>
    [Fact]
    public void ConcreteExample_QuantityOf75_SelectsTier50()
    {
        var precioLista = 10.00m;
        var rules = new List<(decimal CantidadMinima, decimal PrecioEspecial)>
        {
            (10m, 8.00m),
            (50m, 6.00m),
            (100m, 5.00m)
        };

        var result = PricingHelper.DeterminarPrecioEfectivo(precioLista, 75m, rules);

        Assert.Equal(6.00m, result);
    }
}
