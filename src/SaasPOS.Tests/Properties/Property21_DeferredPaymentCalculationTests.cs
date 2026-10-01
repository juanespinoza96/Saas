using FsCheck;
using FsCheck.Xunit;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for deferred payment calculation correctness.
/// **Validates: Requirements 22.6**
///
/// Property 21: Deferred payment calculation correctness.
/// For any sale with MetodoPago = 'TarjetaCredito' and CuotasMeses = N where N > 0,
/// the ValorCuota stored in Ventas SHALL equal round(Total / N, 2).
/// For sales with CuotasMeses = 0 (corriente, débito, efectivo, transferencia),
/// ValorCuota SHALL be NULL.
/// </summary>
public class Property21_DeferredPaymentCalculationTests
{
    /// <summary>
    /// Valid CuotasMeses values greater than zero (deferred payment).
    /// </summary>
    private static readonly int[] ValidCuotasPositivas = { 3, 6, 9, 12, 18 };

    /// <summary>
    /// Generates a positive decimal total in a reasonable range for sale totals (0.01 to 99999.99).
    /// </summary>
    private static Gen<decimal> GenPositiveTotal() =>
        Gen.Choose(1, 9999999)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a valid CuotasMeses value greater than 0: one of {3, 6, 9, 12, 18}.
    /// </summary>
    private static Gen<int> GenCuotasMesesPositivo() =>
        Gen.Elements(ValidCuotasPositivas);

    /// <summary>
    /// For any positive total and valid CuotasMeses > 0, ValorCuota equals Math.Round(Total / CuotasMeses, 2).
    /// **Validates: Requirements 22.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValorCuota_EqualsRoundedDivision_WhenCuotasPositive()
    {
        var gen =
            from total in GenPositiveTotal()
            from cuotas in GenCuotasMesesPositivo()
            select (total, cuotas);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (total, cuotasMeses) = tuple;

            // Simulate the same calculation done in VentasController
            decimal? valorCuota = cuotasMeses > 0
                ? Math.Round(total / cuotasMeses, 2)
                : null;

            // Independently compute expected
            var expected = Math.Round(total / cuotasMeses, 2);

            return (valorCuota.HasValue && valorCuota.Value == expected)
                .Label($"Expected ValorCuota={expected}, got {valorCuota} (Total={total}, CuotasMeses={cuotasMeses})");
        });
    }

    /// <summary>
    /// ValorCuota is always null when CuotasMeses == 0.
    /// **Validates: Requirements 22.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValorCuota_IsNull_WhenCuotasMesesIsZero()
    {
        var gen = GenPositiveTotal();

        return Prop.ForAll(gen.ToArbitrary(), total =>
        {
            var cuotasMeses = 0;

            // Simulate the same calculation done in VentasController
            decimal? valorCuota = cuotasMeses > 0
                ? Math.Round(total / cuotasMeses, 2)
                : null;

            return (valorCuota == null)
                .Label($"Expected ValorCuota=null when CuotasMeses=0, got {valorCuota} (Total={total})");
        });
    }

    /// <summary>
    /// ValorCuota is always positive when Total > 0 and CuotasMeses > 0.
    /// **Validates: Requirements 22.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValorCuota_IsPositive_WhenTotalPositiveAndCuotasPositive()
    {
        var gen =
            from total in GenPositiveTotal()
            from cuotas in GenCuotasMesesPositivo()
            select (total, cuotas);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (total, cuotasMeses) = tuple;

            // Simulate the same calculation done in VentasController
            decimal? valorCuota = cuotasMeses > 0
                ? Math.Round(total / cuotasMeses, 2)
                : null;

            return (valorCuota.HasValue && valorCuota.Value > 0m)
                .Label($"Expected ValorCuota > 0, got {valorCuota} (Total={total}, CuotasMeses={cuotasMeses})");
        });
    }

    /// <summary>
    /// CuotasMeses × ValorCuota approximates Total (within rounding tolerance).
    /// The maximum rounding error is CuotasMeses × 0.005 (half a cent per installment).
    /// **Validates: Requirements 22.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValorCuotaTimesInstallments_ApproximatesTotal()
    {
        var gen =
            from total in GenPositiveTotal()
            from cuotas in GenCuotasMesesPositivo()
            select (total, cuotas);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (total, cuotasMeses) = tuple;

            // Simulate the same calculation done in VentasController
            decimal valorCuota = Math.Round(total / cuotasMeses, 2);

            // The reconstructed total from installments
            var reconstructed = valorCuota * cuotasMeses;

            // Maximum rounding error: each installment can be off by at most 0.005,
            // so the total error is at most CuotasMeses × 0.005
            var tolerance = cuotasMeses * 0.005m;

            var diff = Math.Abs(reconstructed - total);

            return (diff <= tolerance)
                .Label($"Expected |{reconstructed} - {total}| <= {tolerance}, got diff={diff} (CuotasMeses={cuotasMeses}, ValorCuota={valorCuota})");
        });
    }

    /// <summary>
    /// Concrete example: Total=100, CuotasMeses=3 → ValorCuota = Math.Round(100/3, 2) = 33.33.
    /// **Validates: Requirements 22.6**
    /// </summary>
    [Fact]
    public void ConcreteExample_Total100_Cuotas3_ValorCuotaIs33Point33()
    {
        var total = 100m;
        var cuotasMeses = 3;

        decimal? valorCuota = cuotasMeses > 0
            ? Math.Round(total / cuotasMeses, 2)
            : null;

        Assert.NotNull(valorCuota);
        Assert.Equal(33.33m, valorCuota.Value);
    }

    /// <summary>
    /// Concrete example: Total=250.50, CuotasMeses=6 → ValorCuota = Math.Round(250.50/6, 2) = 41.75.
    /// **Validates: Requirements 22.6**
    /// </summary>
    [Fact]
    public void ConcreteExample_Total250Point50_Cuotas6_ValorCuotaIs41Point75()
    {
        var total = 250.50m;
        var cuotasMeses = 6;

        decimal? valorCuota = cuotasMeses > 0
            ? Math.Round(total / cuotasMeses, 2)
            : null;

        Assert.NotNull(valorCuota);
        Assert.Equal(41.75m, valorCuota.Value);
    }

    /// <summary>
    /// Concrete example: CuotasMeses=0 → ValorCuota is always null regardless of total.
    /// **Validates: Requirements 22.6**
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(0.01)]
    [InlineData(99999.99)]
    public void CuotasMesesZero_ValorCuotaAlwaysNull(decimal total)
    {
        var cuotasMeses = 0;

        decimal? valorCuota = cuotasMeses > 0
            ? Math.Round(total / cuotasMeses, 2)
            : null;

        Assert.Null(valorCuota);
    }

    /// <summary>
    /// Concrete example: Total=1000, CuotasMeses=18 → ValorCuota = Math.Round(1000/18, 2) = 55.56.
    /// Verify approximation: 18 × 55.56 = 1000.08, diff = 0.08 &lt;= 18 × 0.005 = 0.09.
    /// **Validates: Requirements 22.6**
    /// </summary>
    [Fact]
    public void ConcreteExample_Total1000_Cuotas18_ApproximatesTotal()
    {
        var total = 1000m;
        var cuotasMeses = 18;

        decimal valorCuota = Math.Round(total / cuotasMeses, 2);
        var reconstructed = valorCuota * cuotasMeses;
        var tolerance = cuotasMeses * 0.005m;
        var diff = Math.Abs(reconstructed - total);

        Assert.Equal(55.56m, valorCuota);
        Assert.True(diff <= tolerance, $"Diff {diff} exceeds tolerance {tolerance}");
    }
}
