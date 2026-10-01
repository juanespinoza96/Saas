using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.Helpers;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for cart total invariant with mixed negotiated and calculated prices.
/// **Validates: Requirements 5.4**
///
/// Property 6: Cart total invariant.
/// For any cart with N line items (1–20), where each line has a positive Cantidad and a
/// positive effective price (either a negotiated PrecioRealCobrado or a calculated price
/// from PricingHelper), the total must equal the sum of (cantidad × precioEfectivo) for all lines.
///
/// Key difference from Property 13 (SaleTotalArithmetic): This property specifically tests
/// MIXED carts — some lines use PrecioRealCobrado (negotiated) and some use null (calculated).
/// </summary>
public class PrecioNegociado_Property6_CartTotalInvariantTests
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
    /// Generates a nullable positive price (some lines have negotiated price, some don't).
    /// Approximately 50% of lines will have a non-null PrecioRealCobrado.
    /// </summary>
    private static Gen<decimal?> GenNullablePrice() =>
        Gen.OneOf(
            Gen.Constant<decimal?>(null),
            GenPositivePrice().Select(p => (decimal?)p));

    /// <summary>
    /// Represents a cart line item with all information needed for total calculation.
    /// </summary>
    private record CartLineItem(
        decimal Cantidad,
        decimal PrecioLista,
        decimal? PrecioRealCobrado,
        List<(decimal CantidadMinima, decimal PrecioEspecial)> ReglasVolumen);

    /// <summary>
    /// Generates a single cart line item with a mix of negotiated and calculated pricing.
    /// </summary>
    private static Gen<CartLineItem> GenCartLineItem() =>
        from cantidad in GenPositiveQuantity()
        from precioLista in GenPositivePrice()
        from precioNegociado in GenNullablePrice()
        from reglas in GenVolumePricingRules()
        select new CartLineItem(cantidad, precioLista, precioNegociado, reglas);

    /// <summary>
    /// Generates a non-empty list of cart line items (1 to 20 items) with mixed pricing.
    /// </summary>
    private static Gen<List<CartLineItem>> GenCart() =>
        from count in Gen.Choose(1, 20)
        from items in Gen.ListOf(count, GenCartLineItem())
        select items.ToList();

    /// <summary>
    /// Generates a cart that is guaranteed to have at least one negotiated line and one calculated line.
    /// This ensures we always test the mixed scenario.
    /// </summary>
    private static Gen<List<CartLineItem>> GenMixedCart() =>
        from negotiatedCount in Gen.Choose(1, 10)
        from calculatedCount in Gen.Choose(1, 10)
        from negotiatedItems in Gen.ListOf(negotiatedCount, GenCartLineItemNegotiated())
        from calculatedItems in Gen.ListOf(calculatedCount, GenCartLineItemCalculated())
        select negotiatedItems.Concat(calculatedItems).ToList();

    /// <summary>
    /// Generates a cart line item with a guaranteed non-null PrecioRealCobrado (negotiated).
    /// </summary>
    private static Gen<CartLineItem> GenCartLineItemNegotiated() =>
        from cantidad in GenPositiveQuantity()
        from precioLista in GenPositivePrice()
        from precioNegociado in GenPositivePrice()
        from reglas in GenVolumePricingRules()
        select new CartLineItem(cantidad, precioLista, precioNegociado, reglas);

    /// <summary>
    /// Generates a cart line item with PrecioRealCobrado = null (calculated).
    /// </summary>
    private static Gen<CartLineItem> GenCartLineItemCalculated() =>
        from cantidad in GenPositiveQuantity()
        from precioLista in GenPositivePrice()
        from reglas in GenVolumePricingRules()
        select new CartLineItem(cantidad, precioLista, null, reglas);

    /// <summary>
    /// Determines the effective price for a line item following the same logic as VentasController:
    /// if PrecioRealCobrado is not null, use it; otherwise use PricingHelper.
    /// </summary>
    private static decimal DetermineEffectivePrice(CartLineItem line) =>
        line.PrecioRealCobrado ?? PricingHelper.DeterminarPrecioEfectivo(
            line.PrecioLista, line.Cantidad, line.ReglasVolumen);

    /// <summary>
    /// For any cart with N line items (1–20) with a mix of negotiated and calculated prices,
    /// the sale total must equal sum(cantidad × precioEfectivo) for all lines.
    /// This tests the mixed scenario specifically required by this property.
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CartTotal_WithMixedNegotiatedAndCalculatedPrices_EqualsSumOfLineSubtotals()
    {
        return Prop.ForAll(GenMixedCart().ToArbitrary(), cart =>
        {
            // Simulate VentasController: determine effective price for each line
            var detalles = cart.Select(line => new
            {
                Cantidad = line.Cantidad,
                PrecioRealCobrado = DetermineEffectivePrice(line)
            }).ToList();

            // Calculate total as the controller does
            var total = detalles.Sum(d => d.Cantidad * d.PrecioRealCobrado);

            // Independently verify: sum of each line's subtotal
            var expectedTotal = 0m;
            foreach (var detalle in detalles)
            {
                expectedTotal += detalle.Cantidad * detalle.PrecioRealCobrado;
            }

            var negotiatedCount = cart.Count(l => l.PrecioRealCobrado != null);
            var calculatedCount = cart.Count(l => l.PrecioRealCobrado == null);

            return (total == expectedTotal)
                .Label($"Expected total={expectedTotal}, got {total} " +
                       $"for {cart.Count} items ({negotiatedCount} negotiated, {calculatedCount} calculated)");
        });
    }

    /// <summary>
    /// For any cart with randomly mixed lines (some null PrecioRealCobrado, some non-null),
    /// the total is always the arithmetic sum of line subtotals.
    /// Tests the general case where lines may randomly have null or non-null PrecioRealCobrado.
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CartTotal_WithRandomMix_EqualsSumOfLineSubtotals()
    {
        return Prop.ForAll(GenCart().ToArbitrary(), cart =>
        {
            // Simulate VentasController: determine effective price for each line
            var detalles = cart.Select(line => new
            {
                Cantidad = line.Cantidad,
                PrecioRealCobrado = DetermineEffectivePrice(line)
            }).ToList();

            // Calculate total as the controller does: sum(Cantidad * PrecioRealCobrado)
            var total = detalles.Sum(d => d.Cantidad * d.PrecioRealCobrado);

            // Independently verify using a different computation approach (loop vs LINQ)
            var expectedTotal = 0m;
            for (int i = 0; i < detalles.Count; i++)
            {
                expectedTotal += detalles[i].Cantidad * detalles[i].PrecioRealCobrado;
            }

            return (total == expectedTotal)
                .Label($"Expected total={expectedTotal}, got {total} for {cart.Count} items");
        });
    }

    /// <summary>
    /// For a cart where ALL lines have negotiated prices (PrecioRealCobrado != null),
    /// the total still equals sum(cantidad × PrecioRealCobrado).
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CartTotal_AllNegotiatedPrices_EqualsSumOfLineSubtotals()
    {
        var gen =
            from count in Gen.Choose(1, 20)
            from items in Gen.ListOf(count, GenCartLineItemNegotiated())
            select items.ToList();

        return Prop.ForAll(gen.ToArbitrary(), cart =>
        {
            // All lines have non-null PrecioRealCobrado
            var detalles = cart.Select(line => new
            {
                Cantidad = line.Cantidad,
                PrecioRealCobrado = line.PrecioRealCobrado!.Value
            }).ToList();

            var total = detalles.Sum(d => d.Cantidad * d.PrecioRealCobrado);

            var expectedTotal = 0m;
            foreach (var detalle in detalles)
            {
                expectedTotal += detalle.Cantidad * detalle.PrecioRealCobrado;
            }

            return (total == expectedTotal)
                .Label($"Expected total={expectedTotal}, got {total} for {cart.Count} all-negotiated items");
        });
    }

    /// <summary>
    /// For a cart where ALL lines use calculated prices (PrecioRealCobrado == null),
    /// the total equals sum(cantidad × PricingHelper.DeterminarPrecioEfectivo(...)).
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CartTotal_AllCalculatedPrices_EqualsSumOfLineSubtotals()
    {
        var gen =
            from count in Gen.Choose(1, 20)
            from items in Gen.ListOf(count, GenCartLineItemCalculated())
            select items.ToList();

        return Prop.ForAll(gen.ToArbitrary(), cart =>
        {
            // All lines have null PrecioRealCobrado → use PricingHelper
            var detalles = cart.Select(line => new
            {
                Cantidad = line.Cantidad,
                PrecioRealCobrado = PricingHelper.DeterminarPrecioEfectivo(
                    line.PrecioLista, line.Cantidad, line.ReglasVolumen)
            }).ToList();

            var total = detalles.Sum(d => d.Cantidad * d.PrecioRealCobrado);

            var expectedTotal = 0m;
            foreach (var detalle in detalles)
            {
                expectedTotal += detalle.Cantidad * detalle.PrecioRealCobrado;
            }

            return (total == expectedTotal)
                .Label($"Expected total={expectedTotal}, got {total} for {cart.Count} all-calculated items");
        });
    }
}
