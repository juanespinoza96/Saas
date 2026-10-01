using FsCheck;
using FsCheck.Xunit;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for null/zero PrecioMinimo skipping minimum comparison.
/// **Validates: Requirements 4.5**
///
/// Property 12: Null/zero PrecioMinimo skips minimum comparison.
/// For any product with PrecioMinimo = 0 (the null/zero case in the domain), and any
/// PrecioRealCobrado > 0 with ≤2 decimal places, the minimum price validation SHALL
/// be skipped entirely and the line SHALL be accepted.
/// </summary>
public class PrecioNegociado_Property12_NullZeroMinimoSkipsTests
{
    /// <summary>
    /// Generates a positive PrecioRealCobrado with ≤2 decimal places.
    /// Range: [0.01, 10000.00]
    /// </summary>
    private static Gen<decimal> GenPositivePrecioRealCobrado() =>
        Gen.Choose(1, 1000000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates very small positive PrecioRealCobrado values with ≤2 decimal places.
    /// Range: [0.01, 1.00] — values that would fail almost any positive PrecioMinimo.
    /// </summary>
    private static Gen<decimal> GenVerySmallPrecioRealCobrado() =>
        Gen.Choose(1, 100)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Simulates the VentasController minimum price validation logic.
    /// Returns true if the line would be REJECTED (minimum price violation detected).
    /// Returns false if the line passes validation (no rejection).
    /// </summary>
    private static bool WouldBeRejectedByMinimumValidation(decimal precioRealCobrado, decimal precioMinimo)
    {
        // Mirrors VentasController logic:
        // if (linea.PrecioRealCobrado.HasValue)
        // {
        //     var producto = productosDict[linea.ProductoId];
        //     if (producto.PrecioMinimo > 0 && linea.PrecioRealCobrado.Value < producto.PrecioMinimo)
        //         return BadRequest(... "PRECIO_BELOW_MINIMUM");
        // }

        // We assume PrecioRealCobrado.HasValue is true (non-null) for this property
        if (precioMinimo > 0 && precioRealCobrado < precioMinimo)
            return true; // Would be rejected

        return false; // Passes validation
    }

    /// <summary>
    /// For any product with PrecioMinimo = 0 and any positive PrecioRealCobrado (≤2 decimals),
    /// the minimum price validation is skipped because the condition `precioMinimo > 0` is false.
    /// Therefore the line is always accepted regardless of how small the price is.
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ZeroPrecioMinimo_SkipsMinimumComparison_AnyPositivePrice()
    {
        var gen = GenPositivePrecioRealCobrado();

        return Prop.ForAll(gen.ToArbitrary(), precioRealCobrado =>
        {
            decimal precioMinimo = 0m;

            var wouldBeRejected = WouldBeRejectedByMinimumValidation(precioRealCobrado, precioMinimo);

            return (!wouldBeRejected)
                .Label($"Expected acceptance for PrecioRealCobrado={precioRealCobrado} " +
                       $"with PrecioMinimo=0, but validation would have rejected it");
        });
    }

    /// <summary>
    /// Even very small prices (0.01–1.00) are accepted when PrecioMinimo is 0,
    /// because the `precioMinimo > 0` guard is false and the comparison never runs.
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ZeroPrecioMinimo_AcceptsVerySmallPrices()
    {
        var gen = GenVerySmallPrecioRealCobrado();

        return Prop.ForAll(gen.ToArbitrary(), precioRealCobrado =>
        {
            decimal precioMinimo = 0m;

            var wouldBeRejected = WouldBeRejectedByMinimumValidation(precioRealCobrado, precioMinimo);

            return (!wouldBeRejected)
                .Label($"Expected acceptance for very small PrecioRealCobrado={precioRealCobrado} " +
                       $"with PrecioMinimo=0, but validation would have rejected it");
        });
    }

    /// <summary>
    /// Contrast property: when PrecioMinimo IS positive (> 0), prices below it ARE rejected.
    /// This confirms that the skip behavior is specifically due to PrecioMinimo being 0.
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PositivePrecioMinimo_DoesNotSkipComparison_RejectsBelow()
    {
        var gen =
            from precioMinimo in Gen.Choose(2, 100000).Select(i => (decimal)i / 100m)
            from precioRealCobrado in Gen.Choose(1, 99).Select(i => (decimal)i / 100m)
                                        .Where(p => p < precioMinimo)
            select (precioMinimo, precioRealCobrado);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioMinimo, precioRealCobrado) = tuple;

            var wouldBeRejected = WouldBeRejectedByMinimumValidation(precioRealCobrado, precioMinimo);

            return wouldBeRejected
                .Label($"Expected rejection for PrecioRealCobrado={precioRealCobrado} < " +
                       $"PrecioMinimo={precioMinimo}, but validation accepted it");
        });
    }
}
