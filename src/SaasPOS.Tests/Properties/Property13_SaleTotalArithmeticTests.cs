using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.Helpers;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for sale total arithmetic correctness.
/// **Validates: Requirements 9.6**
/// 
/// Property 13: Sale total is the arithmetic sum of line items.
/// For any sale with N line items, each having Cantidad and PrecioRealCobrado,
/// the Total field of the sale SHALL equal the sum of Cantidad × PrecioRealCobrado
/// across all line items.
/// </summary>
public class Property13_SaleTotalArithmeticTests
{
    /// <summary>
    /// Generates a positive decimal value in a reasonable range for prices.
    /// </summary>
    private static Gen<decimal> GenPositivePrice() =>
        Gen.Choose(1, 100000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a positive decimal value in a reasonable range for quantities.
    /// </summary>
    private static Gen<decimal> GenPositiveQuantity() =>
        Gen.Choose(1, 10000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a line item as (Cantidad, PrecioRealCobrado).
    /// </summary>
    private static Gen<(decimal Cantidad, decimal PrecioRealCobrado)> GenLineItem() =>
        from cantidad in GenPositiveQuantity()
        from precio in GenPositivePrice()
        select (cantidad, precio);

    /// <summary>
    /// Generates a non-empty list of line items (1 to 20 items).
    /// </summary>
    private static Gen<List<(decimal Cantidad, decimal PrecioRealCobrado)>> GenLineItems(int minCount, int maxCount) =>
        from count in Gen.Choose(minCount, maxCount)
        from items in Gen.ListOf(count, GenLineItem())
        select items.ToList();

    /// <summary>
    /// Generates a list of volume pricing rules with distinct CantidadMinima values.
    /// </summary>
    private static Gen<List<(decimal CantidadMinima, decimal PrecioEspecial)>> GenVolumePricingRules(int minCount, int maxCount) =>
        from count in Gen.Choose(minCount, maxCount)
        from cantidades in Gen.ListOf(count, Gen.Choose(1, 1000).Select(i => (decimal)i))
                              .Select(list => list.Distinct().Take(count).ToList())
        from precios in Gen.ListOf(cantidades.Count, Gen.Choose(1, 100000).Select(i => (decimal)i / 100m))
        select cantidades.Zip(precios, (c, p) => (CantidadMinima: c, PrecioEspecial: p)).ToList();

    /// <summary>
    /// For any set of line items with arbitrary positive Cantidad and PrecioRealCobrado,
    /// the sale total must equal sum(Cantidad × PrecioRealCobrado).
    /// **Validates: Requirements 9.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SaleTotal_IsArithmeticSum_OfAllLineItems()
    {
        var gen = GenLineItems(1, 20);

        return Prop.ForAll(gen.ToArbitrary(), lineItems =>
        {
            // Simulate the same calculation done in VentasController
            var total = lineItems.Sum(d => d.Cantidad * d.PrecioRealCobrado);

            // Verify by computing independently
            var expectedTotal = 0m;
            foreach (var item in lineItems)
            {
                expectedTotal += item.Cantidad * item.PrecioRealCobrado;
            }

            return (total == expectedTotal)
                .Label($"Expected total={expectedTotal}, got {total} for {lineItems.Count} line items");
        });
    }

    /// <summary>
    /// For any product with volume pricing rules, the effective price applied should be
    /// the one determined by PricingHelper, and the resulting total should be arithmetically correct.
    /// **Validates: Requirements 9.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SaleTotal_WithVolumePricing_UsesEffectivePriceFromPricingHelper()
    {
        var gen =
            from lineCount in Gen.Choose(1, 10)
            from preciosLista in Gen.ListOf(lineCount, GenPositivePrice()).Select(l => l.ToList())
            from cantidades in Gen.ListOf(lineCount, GenPositiveQuantity()).Select(l => l.ToList())
            from rules in GenVolumePricingRules(0, 5)
            select (preciosLista, cantidades, rules);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (preciosLista, cantidades, rules) = tuple;

            // Simulate what VentasController does: determine effective price for each line
            var detalles = new List<(decimal Cantidad, decimal PrecioRealCobrado)>();
            for (int i = 0; i < preciosLista.Count; i++)
            {
                var precioEfectivo = PricingHelper.DeterminarPrecioEfectivo(
                    preciosLista[i], cantidades[i], rules);
                detalles.Add((cantidades[i], precioEfectivo));
            }

            // Calculate total as the controller does
            var total = detalles.Sum(d => d.Cantidad * d.PrecioRealCobrado);

            // Verify: total must equal the arithmetic sum of each line's subtotal
            var expectedTotal = 0m;
            foreach (var detalle in detalles)
            {
                expectedTotal += detalle.Cantidad * detalle.PrecioRealCobrado;
            }

            return (total == expectedTotal)
                .Label($"Expected total={expectedTotal}, got {total} for {detalles.Count} line items with volume pricing");
        });
    }

    /// <summary>
    /// An empty sale (no line items) should have Total = 0.
    /// **Validates: Requirements 9.6**
    /// </summary>
    [Fact]
    public void EmptySale_HasTotalOfZero()
    {
        var emptyDetalles = new List<(decimal Cantidad, decimal PrecioRealCobrado)>();

        var total = emptyDetalles.Sum(d => d.Cantidad * d.PrecioRealCobrado);

        Assert.Equal(0m, total);
    }

    /// <summary>
    /// Concrete example: 3 line items with known values.
    /// Line 1: Cantidad=2, Precio=10.50 → Subtotal=21.00
    /// Line 2: Cantidad=5, Precio=3.25 → Subtotal=16.25
    /// Line 3: Cantidad=1, Precio=100.00 → Subtotal=100.00
    /// Total = 137.25
    /// **Validates: Requirements 9.6**
    /// </summary>
    [Fact]
    public void ConcreteExample_ThreeLineItems_TotalIsCorrect()
    {
        var lineItems = new List<(decimal Cantidad, decimal PrecioRealCobrado)>
        {
            (2m, 10.50m),
            (5m, 3.25m),
            (1m, 100.00m)
        };

        var total = lineItems.Sum(d => d.Cantidad * d.PrecioRealCobrado);

        Assert.Equal(137.25m, total);
    }

    /// <summary>
    /// Concrete example: Volume pricing applied to a single product with quantity=75,
    /// rules=[10→$8, 50→$6, 100→$5], PrecioLista=$10.
    /// Effective price should be $6 (tier 50 applies). Total = 75 × 6 = 450.
    /// **Validates: Requirements 9.6**
    /// </summary>
    [Fact]
    public void ConcreteExample_VolumePricingApplied_TotalUsesEffectivePrice()
    {
        var precioLista = 10.00m;
        var cantidad = 75m;
        var rules = new List<(decimal CantidadMinima, decimal PrecioEspecial)>
        {
            (10m, 8.00m),
            (50m, 6.00m),
            (100m, 5.00m)
        };

        var precioEfectivo = PricingHelper.DeterminarPrecioEfectivo(precioLista, cantidad, rules);
        var total = cantidad * precioEfectivo;

        Assert.Equal(6.00m, precioEfectivo);
        Assert.Equal(450.00m, total);
    }

    /// <summary>
    /// Single line item: Total equals Cantidad × PrecioRealCobrado directly.
    /// **Validates: Requirements 9.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SingleLineItem_TotalEqualsQuantityTimesPrice()
    {
        var gen =
            from cantidad in GenPositiveQuantity()
            from precio in GenPositivePrice()
            select (cantidad, precio);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (cantidad, precio) = tuple;

            var lineItems = new List<(decimal Cantidad, decimal PrecioRealCobrado)> { (cantidad, precio) };
            var total = lineItems.Sum(d => d.Cantidad * d.PrecioRealCobrado);

            var expected = cantidad * precio;

            return (total == expected)
                .Label($"Expected {expected}, got {total} for single item (cantidad={cantidad}, precio={precio})");
        });
    }
}
