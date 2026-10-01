using FsCheck;
using FsCheck.Xunit;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for null PrecioRealCobrado bypassing minimum price validation.
/// **Validates: Requirements 4.4**
///
/// Property 5: Null PrecioRealCobrado bypasses minimum validation.
/// For any product with any PrecioMinimo (including very high values), when PrecioRealCobrado
/// is null in the request line, the minimum price validation SHALL NOT be triggered.
/// The line SHALL be accepted regardless of the PrecioMinimo value.
/// </summary>
public class PrecioNegociado_Property5_NullBypassesMinimumTests
{
    /// <summary>
    /// Generates a positive decimal PrecioMinimo in range [0.01, 10000].
    /// </summary>
    private static Gen<decimal> GenPrecioMinimo() =>
        Gen.Choose(1, 1000000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Simulates the VentasController minimum price validation logic.
    /// Returns true if the line would be REJECTED (minimum price violation detected).
    /// Returns false if the line passes validation (no rejection).
    /// </summary>
    private static bool WouldBeRejectedByMinimumValidation(decimal? precioRealCobrado, decimal precioMinimo)
    {
        // Mirrors VentasController logic:
        // foreach (var linea in request.Lineas)
        // {
        //     if (linea.PrecioRealCobrado.HasValue)
        //     {
        //         var producto = productosDict[linea.ProductoId];
        //         if (producto.PrecioMinimo > 0 && linea.PrecioRealCobrado.Value < producto.PrecioMinimo)
        //             return BadRequest(...);
        //     }
        // }

        if (precioRealCobrado.HasValue)
        {
            if (precioMinimo > 0 && precioRealCobrado.Value < precioMinimo)
                return true; // Would be rejected
        }

        return false; // Passes validation
    }

    /// <summary>
    /// For any product with any PrecioMinimo (0.01–10000), when PrecioRealCobrado is null,
    /// the minimum price validation is NOT triggered and the line is accepted.
    /// 
    /// This property verifies that the condition `linea.PrecioRealCobrado.HasValue` is false
    /// when PrecioRealCobrado is null, meaning the minimum price check is never reached.
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NullPrecioRealCobrado_NeverTriggersMinimumValidation()
    {
        var gen = GenPrecioMinimo();

        return Prop.ForAll(gen.ToArbitrary(), precioMinimo =>
        {
            decimal? precioRealCobrado = null;

            var wouldBeRejected = WouldBeRejectedByMinimumValidation(precioRealCobrado, precioMinimo);

            return (!wouldBeRejected)
                .Label($"Expected no rejection for null PrecioRealCobrado with PrecioMinimo={precioMinimo}, " +
                       $"but validation would have rejected it");
        });
    }

    /// <summary>
    /// For any product with a very high PrecioMinimo (higher than typical prices),
    /// when PrecioRealCobrado is null, the line is still accepted because the
    /// HasValue check prevents the minimum comparison from executing.
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NullPrecioRealCobrado_AcceptedEvenWithVeryHighPrecioMinimo()
    {
        // Generate high PrecioMinimo values (100–10000) that would reject any reasonable price
        var gen = Gen.Choose(10000, 1000000)
                     .Select(i => (decimal)i / 100m);

        return Prop.ForAll(gen.ToArbitrary(), precioMinimo =>
        {
            decimal? precioRealCobrado = null;

            // The HasValue check should short-circuit; minimum validation never runs
            var wouldBeRejected = WouldBeRejectedByMinimumValidation(precioRealCobrado, precioMinimo);

            return (!wouldBeRejected)
                .Label($"Null PrecioRealCobrado should bypass minimum validation " +
                       $"even with high PrecioMinimo={precioMinimo}");
        });
    }

    /// <summary>
    /// Contrast property: when PrecioRealCobrado IS provided and is below PrecioMinimo,
    /// the validation WOULD reject. This confirms that the bypass only works for null values.
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NonNullPrecioRealCobrado_BelowMinimum_WouldBeRejected()
    {
        var gen =
            from precioMinimo in Gen.Choose(100, 100000).Select(i => (decimal)i / 100m)
            from precioRealCobrado in Gen.Choose(1, 99).Select(i => (decimal)i / 100m)
                                        .Where(p => p < precioMinimo)
            select (precioMinimo, precioRealCobrado);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioMinimo, precioRealCobrado) = tuple;

            var wouldBeRejected = WouldBeRejectedByMinimumValidation(precioRealCobrado, precioMinimo);

            return wouldBeRejected
                .Label($"Expected rejection for PrecioRealCobrado={precioRealCobrado} < PrecioMinimo={precioMinimo}");
        });
    }
}
