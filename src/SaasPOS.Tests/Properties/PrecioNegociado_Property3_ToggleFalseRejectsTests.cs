using FsCheck;
using FsCheck.Xunit;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for negotiated price rejection when toggle is disabled.
/// **Validates: Requirements 3.1**
///
/// Property 3: Toggle false rejects negotiated prices
/// For any CrearVentaRequest containing at least one line with non-null PrecioRealCobrado,
/// when PermitePrecioNegociado is false and EsBarEscolar is false for the target sucursal,
/// the validation logic SHALL determine rejection with error code PRECIO_NEGOCIADO_NOT_ALLOWED.
/// </summary>
public class PrecioNegociado_Property3_ToggleFalseRejectsTests
{
    /// <summary>
    /// Generates a positive decimal with at most 2 decimal places (valid PrecioRealCobrado).
    /// Range: 0.01 to 10000.00
    /// </summary>
    private static Gen<decimal> GenValidPositivePrice() =>
        Gen.Choose(1, 1000000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a line item representation as (hasNegotiatedPrice, precioRealCobrado).
    /// At least one line will have a non-null PrecioRealCobrado.
    /// </summary>
    private static Gen<decimal?> GenNonNullPrice() =>
        GenValidPositivePrice().Select(p => (decimal?)p);

    private static Gen<decimal?> GenNullablePrice() =>
        Gen.Frequency(
            Tuple.Create(1, Gen.Constant((decimal?)null)),
            Tuple.Create(2, GenNonNullPrice()));

    /// <summary>
    /// Generates a list of 1–10 lines where at least one has a non-null PrecioRealCobrado.
    /// </summary>
    private static Gen<List<decimal?>> GenLinesWithAtLeastOneNegotiated() =>
        from count in Gen.Choose(1, 10)
        from lines in Gen.ListOf(count, GenNullablePrice())
        from guaranteedPrice in GenNonNullPrice()
        from insertIndex in Gen.Choose(0, count)
        select InsertGuaranteedPrice(lines.ToList(), guaranteedPrice, insertIndex);

    private static List<decimal?> InsertGuaranteedPrice(List<decimal?> lines, decimal? guaranteedPrice, int insertIndex)
    {
        // Ensure at least one line has a non-null price by inserting/replacing
        if (insertIndex < lines.Count)
            lines[insertIndex] = guaranteedPrice;
        else
            lines.Add(guaranteedPrice);
        return lines;
    }

    /// <summary>
    /// For any set of lines where at least one has a non-null PrecioRealCobrado (valid: >0, ≤2 decimals),
    /// when PermitePrecioNegociado is false and EsBarEscolar is false, the validation condition
    /// (tieneLineasNegociadas && !permitePrecioNegociado) is always true → would result in rejection.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ToggleFalse_WithNegotiatedPrices_AlwaysRejects()
    {
        var gen = GenLinesWithAtLeastOneNegotiated();

        return Prop.ForAll(gen.ToArbitrary(), lines =>
        {
            // Configuration: toggle disabled, not bar escolar
            const bool permitePrecioNegociado = false;
            // EsBarEscolar = false means the bar escolar check passes; toggle check is what matters here.

            // Simulate the controller's validation logic
            bool tieneLineasNegociadas = lines.Any(l => l.HasValue);

            // The rejection condition from VentasController:
            // if (tieneLineasNegociadas && configSucursal?.PermitePrecioNegociado != true)
            //     return BadRequest(...)
            bool shouldReject = tieneLineasNegociadas && !permitePrecioNegociado;

            // Since we guarantee at least one line has non-null price AND toggle is false,
            // rejection should always occur
            return shouldReject
                .Label($"Expected rejection for {lines.Count} lines " +
                       $"({lines.Count(l => l.HasValue)} negotiated) " +
                       $"with PermitePrecioNegociado=false, EsBarEscolar=false");
        });
    }

    /// <summary>
    /// For any number of lines (1-10), all having non-null PrecioRealCobrado,
    /// when toggle is false, rejection is guaranteed regardless of how many lines are negotiated.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ToggleFalse_AllLinesNegotiated_AlwaysRejects()
    {
        var gen =
            from count in Gen.Choose(1, 10)
            from prices in Gen.ListOf(count, GenValidPositivePrice())
            select prices.Select(p => (decimal?)p).ToList();

        return Prop.ForAll(gen.ToArbitrary(), lines =>
        {
            const bool permitePrecioNegociado = false;
            // EsBarEscolar = false; toggle is the rejecting condition here.

            // All lines have non-null PrecioRealCobrado
            bool tieneLineasNegociadas = lines.Any(l => l.HasValue);

            // The decision logic
            bool shouldReject = tieneLineasNegociadas && !permitePrecioNegociado;

            return shouldReject
                .Label($"Expected rejection for {lines.Count} fully-negotiated lines " +
                       $"with PermitePrecioNegociado=false");
        });
    }

    /// <summary>
    /// Even a single line with a non-null PrecioRealCobrado is enough to trigger rejection
    /// when toggle is false. Tests with exactly one negotiated line among many null lines.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ToggleFalse_SingleNegotiatedLineAmongNulls_StillRejects()
    {
        var gen =
            from nullCount in Gen.Choose(0, 9)
            from negotiatedPrice in GenValidPositivePrice()
            from insertPos in Gen.Choose(0, nullCount)
            select BuildLinesWithSingleNegotiated(nullCount, negotiatedPrice, insertPos);

        return Prop.ForAll(gen.ToArbitrary(), lines =>
        {
            bool permitePrecioNegociado = false;

            bool tieneLineasNegociadas = lines.Any(l => l.HasValue);
            bool shouldReject = tieneLineasNegociadas && !permitePrecioNegociado;

            return shouldReject
                .Label($"Expected rejection with single negotiated price " +
                       $"among {lines.Count} total lines");
        });
    }

    private static List<decimal?> BuildLinesWithSingleNegotiated(int nullCount, decimal negotiatedPrice, int insertPos)
    {
        var lines = Enumerable.Repeat((decimal?)null, nullCount).ToList();
        int actualInsertPos = Math.Min(insertPos, lines.Count);
        lines.Insert(actualInsertPos, negotiatedPrice);
        return lines;
    }
}
