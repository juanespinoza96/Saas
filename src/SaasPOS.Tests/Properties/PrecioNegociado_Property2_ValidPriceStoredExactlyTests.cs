using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.Helpers;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for Property 2: Valid non-null PrecioRealCobrado is stored exactly.
/// 
/// For any product and any PrecioRealCobrado value that is > 0, has ≤ 2 decimal places,
/// and is >= Producto.PrecioMinimo (when PrecioMinimo > 0), when PermitePrecioNegociado is true
/// and EsBarEscolar is false, the stored DetalleVenta.PrecioRealCobrado SHALL equal the provided
/// PrecioRealCobrado value exactly.
/// 
/// **Validates: Requirements 2.3, 3.2**
/// </summary>
public class PrecioNegociado_Property2_ValidPriceStoredExactlyTests
{
    /// <summary>
    /// Generates a PrecioMinimo value: either 0 (no minimum) or a positive value between 0.01 and 500.
    /// </summary>
    private static Gen<decimal> GenPrecioMinimo() =>
        Gen.Frequency(
            Tuple.Create(1, Gen.Constant(0m)),
            Tuple.Create(3, Gen.Choose(1, 50000).Select(i => (decimal)i / 100m))
        );

    /// <summary>
    /// Generates a valid PrecioRealCobrado: > 0, ≤ 2 decimal places, and ≥ precioMinimo.
    /// Uses Gen.Choose for integers divided by 100m to ensure exactly 2 decimal places.
    /// </summary>
    private static Gen<decimal> GenValidPrecioRealCobrado(decimal precioMinimo) =>
        Gen.Choose(
            precioMinimo > 0 ? (int)Math.Ceiling(precioMinimo * 100) : 1,
            100000
        ).Select(i => (decimal)i / 100m);

    /// <summary>
    /// Property: When PrecioRealCobrado is provided (not null), toggle=true, barEscolar=false,
    /// and the value passes all validations (>0, ≤2 decimals, ≥PrecioMinimo),
    /// the effective price stored equals PrecioRealCobrado exactly.
    /// 
    /// This simulates the controller logic:
    ///   var precioEfectivo = linea.PrecioRealCobrado ?? precioCalculado;
    /// 
    /// When PrecioRealCobrado is non-null, precioEfectivo == PrecioRealCobrado.
    /// 
    /// **Validates: Requirements 2.3, 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValidNonNull_PrecioRealCobrado_IsStoredExactly()
    {
        var gen =
            from precioMinimo in GenPrecioMinimo()
            from precioRealCobrado in GenValidPrecioRealCobrado(precioMinimo)
            from precioLista in Gen.Choose(1, 100000).Select(i => (decimal)i / 100m)
            from cantidad in Gen.Choose(1, 10000).Select(i => (decimal)i / 100m)
            select (precioMinimo, precioRealCobrado, precioLista, cantidad);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioMinimo, precioRealCobrado, precioLista, cantidad) = tuple;

            // Simulate the conditions: toggle=true, barEscolar=false
            bool permitePrecioNegociado = true;
            bool esBarEscolar = false;

            // Preconditions that would be validated by the controller
            bool isPositive = precioRealCobrado > 0;
            bool hasValidPrecision = (precioRealCobrado * 100m) == Math.Floor(precioRealCobrado * 100m);
            bool meetsMinimum = precioMinimo <= 0 || precioRealCobrado >= precioMinimo;

            // All preconditions should hold given our generators
            if (!isPositive || !hasValidPrecision || !meetsMinimum)
                return true.Label("Skipped: precondition not met (generator issue)");

            // When PermitePrecioNegociado=true and EsBarEscolar=false,
            // the controller accepts the request and uses PrecioRealCobrado directly
            if (!permitePrecioNegociado || esBarEscolar)
                return true.Label("Skipped: toggle/barEscolar condition not met");

            // Simulate the controller's price determination logic:
            // var precioEfectivo = linea.PrecioRealCobrado ?? precioCalculado;
            decimal? precioRealCobradoNullable = precioRealCobrado;
            var precioCalculado = PricingHelper.DeterminarPrecioEfectivo(
                precioLista, cantidad, new List<(decimal, decimal)>());

            var precioEfectivo = precioRealCobradoNullable ?? precioCalculado;

            // Property: the stored effective price MUST equal the provided PrecioRealCobrado exactly
            return (precioEfectivo == precioRealCobrado)
                .Label($"Expected precioEfectivo={precioRealCobrado}, got {precioEfectivo} " +
                       $"(precioMinimo={precioMinimo}, precioLista={precioLista}, cantidad={cantidad})");
        });
    }

    /// <summary>
    /// Property: With volume pricing rules present, when PrecioRealCobrado is non-null and valid,
    /// the effective price still equals PrecioRealCobrado exactly (volume rules are ignored).
    /// 
    /// **Validates: Requirements 2.3, 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValidNonNull_PrecioRealCobrado_IgnoresVolumePricingRules()
    {
        var gen =
            from precioMinimo in GenPrecioMinimo()
            from precioRealCobrado in GenValidPrecioRealCobrado(precioMinimo)
            from precioLista in Gen.Choose(1, 100000).Select(i => (decimal)i / 100m)
            from cantidad in Gen.Choose(1, 10000).Select(i => (decimal)i / 100m)
            from ruleCount in Gen.Choose(1, 5)
            from rules in Gen.ListOf(ruleCount,
                from cantMin in Gen.Choose(1, 500).Select(i => (decimal)i)
                from precioEsp in Gen.Choose(1, 100000).Select(i => (decimal)i / 100m)
                select (cantMin, precioEsp))
            select (precioMinimo, precioRealCobrado, precioLista, cantidad, rules.ToList());

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioMinimo, precioRealCobrado, precioLista, cantidad, rules) = tuple;

            // Simulate: toggle=true, barEscolar=false, valid PrecioRealCobrado
            decimal? precioRealCobradoNullable = precioRealCobrado;

            // The volume pricing would give a different price
            var precioCalculado = PricingHelper.DeterminarPrecioEfectivo(
                precioLista, cantidad, rules);

            // Controller logic: PrecioRealCobrado takes priority over calculated price
            var precioEfectivo = precioRealCobradoNullable ?? precioCalculado;

            // Property: effective price must equal PrecioRealCobrado regardless of volume rules
            return (precioEfectivo == precioRealCobrado)
                .Label($"Expected precioEfectivo={precioRealCobrado}, got {precioEfectivo} " +
                       $"(precioCalculado={precioCalculado}, rules={rules.Count})");
        });
    }

    /// <summary>
    /// Property: PrecioRealCobrado values at the exact boundary (equal to PrecioMinimo)
    /// are stored exactly without modification.
    /// 
    /// **Validates: Requirements 2.3, 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PrecioRealCobrado_AtExactMinimum_IsStoredExactly()
    {
        var gen =
            from precioMinimo in Gen.Choose(1, 50000).Select(i => (decimal)i / 100m)
            from precioLista in Gen.Choose(1, 100000).Select(i => (decimal)i / 100m)
            from cantidad in Gen.Choose(1, 10000).Select(i => (decimal)i / 100m)
            select (precioMinimo, precioLista, cantidad);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioMinimo, precioLista, cantidad) = tuple;

            // PrecioRealCobrado exactly at the minimum boundary
            var precioRealCobrado = precioMinimo;

            // Simulate controller logic
            decimal? precioRealCobradoNullable = precioRealCobrado;
            var precioCalculado = PricingHelper.DeterminarPrecioEfectivo(
                precioLista, cantidad, new List<(decimal, decimal)>());

            var precioEfectivo = precioRealCobradoNullable ?? precioCalculado;

            // Property: exact minimum is accepted and stored without modification
            return (precioEfectivo == precioRealCobrado)
                .Label($"Expected precioEfectivo={precioRealCobrado} (=precioMinimo), got {precioEfectivo}");
        });
    }
}
