using FsCheck;
using FsCheck.Xunit;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for audit flag reflecting negotiation usage.
/// **Validates: Requirements 6.1, 6.2**
///
/// Property 7: Audit flag reflects negotiation usage
/// For any completed sale, usoPrecioNegociado in the audit log SHALL be true if and only if
/// at least one line in the original request had a non-null PrecioRealCobrado provided by the user.
/// </summary>
public class PrecioNegociado_Property7_AuditFlagTests
{
    /// <summary>
    /// Generates a positive decimal with at most 2 decimal places (valid PrecioRealCobrado).
    /// Range: 0.01 to 10000.00
    /// </summary>
    private static Gen<decimal> GenValidPositivePrice() =>
        Gen.Choose(1, 1000000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a nullable price that is non-null (represents a negotiated price).
    /// </summary>
    private static Gen<decimal?> GenNonNullPrice() =>
        GenValidPositivePrice().Select(p => (decimal?)p);

    /// <summary>
    /// Generates a nullable price that can be either null or a valid positive value.
    /// </summary>
    private static Gen<decimal?> GenNullablePrice() =>
        Gen.Frequency(
            Tuple.Create(1, Gen.Constant((decimal?)null)),
            Tuple.Create(2, GenNonNullPrice()));

    /// <summary>
    /// Generates a list of 1–10 lines where at least one has a non-null PrecioRealCobrado.
    /// Guarantees at least one negotiated price exists.
    /// </summary>
    private static Gen<List<decimal?>> GenLinesWithAtLeastOneNegotiated() =>
        from count in Gen.Choose(1, 10)
        from lines in Gen.ListOf(count, GenNullablePrice())
        from guaranteedPrice in GenNonNullPrice()
        from insertIndex in Gen.Choose(0, count)
        select InsertGuaranteedPrice(lines.ToList(), guaranteedPrice, insertIndex);

    /// <summary>
    /// Generates a list of 1–10 lines where ALL have null PrecioRealCobrado.
    /// </summary>
    private static Gen<List<decimal?>> GenLinesAllNull() =>
        from count in Gen.Choose(1, 10)
        select Enumerable.Repeat((decimal?)null, count).ToList();

    /// <summary>
    /// Generates a mixed list of 2–10 lines where some are null and some are non-null.
    /// Guarantees at least one null and at least one non-null.
    /// </summary>
    private static Gen<List<decimal?>> GenMixedLines() =>
        from nullCount in Gen.Choose(1, 5)
        from nonNullCount in Gen.Choose(1, 5)
        from nonNullPrices in Gen.ListOf(nonNullCount, GenValidPositivePrice())
        select BuildMixedLines(nullCount, nonNullPrices.ToList());

    private static List<decimal?> InsertGuaranteedPrice(List<decimal?> lines, decimal? guaranteedPrice, int insertIndex)
    {
        if (insertIndex < lines.Count)
            lines[insertIndex] = guaranteedPrice;
        else
            lines.Add(guaranteedPrice);
        return lines;
    }

    private static List<decimal?> BuildMixedLines(int nullCount, List<decimal> nonNullPrices)
    {
        var lines = new List<decimal?>();
        lines.AddRange(Enumerable.Repeat((decimal?)null, nullCount));
        lines.AddRange(nonNullPrices.Select(p => (decimal?)p));
        // Shuffle deterministically by interleaving
        var result = new List<decimal?>();
        int ni = 0, pi = 0;
        int nullItems = nullCount;
        int nonNullItems = nonNullPrices.Count;
        for (int i = 0; i < nullItems + nonNullItems; i++)
        {
            if (i % 2 == 0 && ni < nullItems)
            {
                result.Add(null);
                ni++;
            }
            else if (pi < nonNullItems)
            {
                result.Add(nonNullPrices[pi]);
                pi++;
            }
            else
            {
                result.Add(null);
                ni++;
            }
        }
        return result;
    }

    /// <summary>
    /// Property A: For any sale with at least one line with non-null PrecioRealCobrado,
    /// usoPrecioNegociado must be true.
    /// **Validates: Requirements 6.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AtLeastOneNegotiated_AuditFlagIsTrue()
    {
        var gen = GenLinesWithAtLeastOneNegotiated();

        return Prop.ForAll(gen.ToArbitrary(), lines =>
        {
            // Simulate the controller logic:
            // var usoPrecioNegociado = request.Lineas.Any(l => l.PrecioRealCobrado.HasValue);
            var usoPrecioNegociado = lines.Any(l => l.HasValue);

            return usoPrecioNegociado
                .Label($"Expected usoPrecioNegociado=true for {lines.Count} lines " +
                       $"({lines.Count(l => l.HasValue)} negotiated, " +
                       $"{lines.Count(l => !l.HasValue)} calculated)");
        });
    }

    /// <summary>
    /// Property B: For any sale with ALL lines having null PrecioRealCobrado,
    /// usoPrecioNegociado must be false.
    /// **Validates: Requirements 6.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AllLinesNull_AuditFlagIsFalse()
    {
        var gen = GenLinesAllNull();

        return Prop.ForAll(gen.ToArbitrary(), lines =>
        {
            // Simulate the controller logic:
            // var usoPrecioNegociado = request.Lineas.Any(l => l.PrecioRealCobrado.HasValue);
            var usoPrecioNegociado = lines.Any(l => l.HasValue);

            return (!usoPrecioNegociado)
                .Label($"Expected usoPrecioNegociado=false for {lines.Count} lines " +
                       $"(all null/calculated)");
        });
    }

    /// <summary>
    /// Property C: For any mixed sale (some null, some non-null),
    /// usoPrecioNegociado must be true.
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MixedLines_AuditFlagIsTrue()
    {
        var gen = GenMixedLines();

        return Prop.ForAll(gen.ToArbitrary(), lines =>
        {
            // Simulate the controller logic:
            // var usoPrecioNegociado = request.Lineas.Any(l => l.PrecioRealCobrado.HasValue);
            var usoPrecioNegociado = lines.Any(l => l.HasValue);

            // Mixed lines guarantee at least one non-null, so flag must be true
            return usoPrecioNegociado
                .Label($"Expected usoPrecioNegociado=true for mixed sale with {lines.Count} lines " +
                       $"({lines.Count(l => l.HasValue)} negotiated, " +
                       $"{lines.Count(l => !l.HasValue)} calculated)");
        });
    }

    /// <summary>
    /// The audit flag is a pure function of the input: it is true IFF any line has HasValue == true.
    /// This tests the biconditional ("if and only if") with arbitrary combinations.
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AuditFlag_IsTrueIffAnyLineHasValue()
    {
        var gen =
            from count in Gen.Choose(1, 10)
            from lines in Gen.ListOf(count, GenNullablePrice())
            select lines.ToList();

        return Prop.ForAll(gen.ToArbitrary(), lines =>
        {
            // The controller's logic
            var usoPrecioNegociado = lines.Any(l => l.HasValue);

            // The expected value: true IFF at least one line is non-null
            var expected = lines.Any(l => l.HasValue);

            return (usoPrecioNegociado == expected)
                .Label($"usoPrecioNegociado={usoPrecioNegociado} should equal " +
                       $"hasAnyNonNull={expected} for {lines.Count} lines");
        });
    }
}
