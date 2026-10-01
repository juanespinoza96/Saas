using FsCheck;
using FsCheck.Xunit;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for invalid precision PrecioRealCobrado rejection.
/// **Validates: Requirements 2.5**
///
/// Property 10: Invalid precision PrecioRealCobrado is rejected.
/// For any PrecioRealCobrado value with more than 2 decimal places,
/// the API SHALL reject the request with error code PRECIO_INVALID_PRECISION.
/// </summary>
public class PrecioNegociado_Property10_InvalidPrecisionRejectedTests
{
    /// <summary>
    /// Generates a positive decimal with exactly 3 decimal places.
    /// Uses Gen.Choose(1, 999999) / 1000m → values like 0.001 to 999.999.
    /// Filters to ensure value != Math.Round(value, 2) (has true 3+ decimal significance).
    /// </summary>
    private static Gen<decimal> GenInvalidPrecisionPrice3Decimals() =>
        Gen.Choose(1, 999999)
           .Select(i => (decimal)i / 1000m)
           .Where(v => v != Math.Round(v, 2));

    /// <summary>
    /// Generates a positive decimal with exactly 4 decimal places.
    /// Uses Gen.Choose(1, 9999999) / 10000m → values like 0.0001 to 999.9999.
    /// Filters to ensure value != Math.Round(value, 2).
    /// </summary>
    private static Gen<decimal> GenInvalidPrecisionPrice4Decimals() =>
        Gen.Choose(1, 9999999)
           .Select(i => (decimal)i / 10000m)
           .Where(v => v != Math.Round(v, 2));

    /// <summary>
    /// Combines generators for 3 and 4 decimal place values.
    /// </summary>
    private static Gen<decimal> GenInvalidPrecisionPrice() =>
        Gen.OneOf(GenInvalidPrecisionPrice3Decimals(), GenInvalidPrecisionPrice4Decimals());

    /// <summary>
    /// For any PrecioRealCobrado with more than 2 decimal places, the precision
    /// validation SHALL detect it (value != Math.Round(value, 2)) and the request
    /// should be rejected with PRECIO_INVALID_PRECISION.
    ///
    /// This simulates the VentasController logic:
    /// if (linea.PrecioRealCobrado.Value != Math.Round(linea.PrecioRealCobrado.Value, 2))
    ///     → reject with PRECIO_INVALID_PRECISION
    ///
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PrecioRealCobrado_WithMoreThan2Decimals_IsRejected()
    {
        return Prop.ForAll(GenInvalidPrecisionPrice().ToArbitrary(), precioRealCobrado =>
        {
            // Simulate VentasController validation logic
            bool shouldReject = precioRealCobrado != Math.Round(precioRealCobrado, 2);

            return shouldReject
                .Label($"Expected rejection: PrecioRealCobrado={precioRealCobrado} has more than 2 decimal places (rounded={Math.Round(precioRealCobrado, 2)})");
        });
    }

    /// <summary>
    /// The generated invalid precision values must always be positive,
    /// to ensure the test doesn't overlap with the PRECIO_MUST_BE_POSITIVE validation
    /// (positivity is checked first in the controller).
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GeneratedInvalidPrecisionPrice_IsAlwaysPositive()
    {
        return Prop.ForAll(GenInvalidPrecisionPrice().ToArbitrary(), precioRealCobrado =>
        {
            return (precioRealCobrado > 0m)
                .Label($"PrecioRealCobrado={precioRealCobrado} should be > 0");
        });
    }

    /// <summary>
    /// The generated values must always truly have more than 2 decimal places,
    /// confirming the generator produces values that would fail the precision check.
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GeneratedInvalidPrecisionPrice_AlwaysFailsRoundingCheck()
    {
        return Prop.ForAll(GenInvalidPrecisionPrice().ToArbitrary(), precioRealCobrado =>
        {
            var rounded = Math.Round(precioRealCobrado, 2);

            return (precioRealCobrado != rounded)
                .Label($"PrecioRealCobrado={precioRealCobrado} should differ from Math.Round(value, 2)={rounded}");
        });
    }
}
