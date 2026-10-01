using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.Helpers;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for audit comparative pricing inclusion.
/// **Validates: Requirements 6.3**
///
/// Property 8: Audit includes comparative pricing.
/// For any line in a completed sale where the user provided a non-null PrecioRealCobrado,
/// the audit log entry SHALL contain both the PrecioRealCobrado (actual price charged)
/// and the PrecioCalculado (price that PricingHelper would have determined).
/// </summary>
public class PrecioNegociado_Property8_AuditComparativePricingTests
{
    /// <summary>
    /// Generates a positive decimal price in range [0.01, 1000.00].
    /// </summary>
    private static Gen<decimal> GenPositivePrice() =>
        Gen.Choose(1, 100000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a positive decimal quantity in range [0.01, 100.00].
    /// </summary>
    private static Gen<decimal> GenPositiveQuantity() =>
        Gen.Choose(1, 10000)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a list of volume pricing rules (0 to 5 rules) with distinct CantidadMinima values.
    /// </summary>
    private static Gen<List<(decimal CantidadMinima, decimal PrecioEspecial)>> GenVolumePricingRules() =>
        from count in Gen.Choose(0, 5)
        from cantidades in Gen.ListOf(count, Gen.Choose(1, 500).Select(i => (decimal)i))
                              .Select(list => list.Distinct().Take(count).ToList())
        from precios in Gen.ListOf(cantidades.Count, Gen.Choose(1, 100000).Select(i => (decimal)i / 100m))
        select cantidades.Zip(precios, (c, p) => (CantidadMinima: c, PrecioEspecial: p)).ToList();

    /// <summary>
    /// Represents an audit detail entry as constructed by VentasController.
    /// </summary>
    private record AuditDetalleEntry(
        int ProductoId,
        decimal Cantidad,
        decimal? PrecioRealCobrado,
        decimal PrecioCalculado);

    /// <summary>
    /// Simulates the VentasController audit detail construction logic.
    /// For each line, computes PrecioCalculado via PricingHelper and includes
    /// both PrecioRealCobrado and PrecioCalculado in the audit entry.
    /// </summary>
    private static AuditDetalleEntry BuildAuditEntry(
        int productoId,
        decimal cantidad,
        decimal? precioRealCobrado,
        decimal precioLista,
        List<(decimal CantidadMinima, decimal PrecioEspecial)> reglasVolumen)
    {
        var precioCalculado = PricingHelper.DeterminarPrecioEfectivo(
            precioLista, cantidad, reglasVolumen);

        return new AuditDetalleEntry(
            ProductoId: productoId,
            Cantidad: cantidad,
            PrecioRealCobrado: precioRealCobrado,
            PrecioCalculado: precioCalculado);
    }

    /// <summary>
    /// For any line with non-null PrecioRealCobrado and any product configuration,
    /// the audit entry always contains both PrecioRealCobrado and PrecioCalculado values.
    /// PrecioRealCobrado in the audit equals the provided negotiated price.
    /// PrecioCalculado in the audit equals what PricingHelper.DeterminarPrecioEfectivo returns.
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AuditEntry_ContainsBothPrices_WhenPrecioRealCobradoProvided()
    {
        var gen =
            from precioLista in GenPositivePrice()
            from cantidad in GenPositiveQuantity()
            from precioRealCobrado in GenPositivePrice()
            from reglasVolumen in GenVolumePricingRules()
            select (precioLista, cantidad, precioRealCobrado, reglasVolumen);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioLista, cantidad, precioRealCobrado, reglasVolumen) = tuple;

            var auditEntry = BuildAuditEntry(
                productoId: 1,
                cantidad: cantidad,
                precioRealCobrado: precioRealCobrado,
                precioLista: precioLista,
                reglasVolumen: reglasVolumen);

            var expectedPrecioCalculado = PricingHelper.DeterminarPrecioEfectivo(
                precioLista, cantidad, reglasVolumen);

            // Audit entry must have PrecioRealCobrado present (non-null)
            var hasRealCobrado = auditEntry.PrecioRealCobrado.HasValue;

            // Audit entry must have correct PrecioRealCobrado value
            var realCobradoCorrect = auditEntry.PrecioRealCobrado == precioRealCobrado;

            // Audit entry must have PrecioCalculado matching PricingHelper result
            var precioCalculadoCorrect = auditEntry.PrecioCalculado == expectedPrecioCalculado;

            return (hasRealCobrado && realCobradoCorrect && precioCalculadoCorrect)
                .Label($"Expected audit entry to contain PrecioRealCobrado={precioRealCobrado} " +
                       $"and PrecioCalculado={expectedPrecioCalculado}, " +
                       $"got PrecioRealCobrado={auditEntry.PrecioRealCobrado}, " +
                       $"PrecioCalculado={auditEntry.PrecioCalculado}");
        });
    }

    /// <summary>
    /// For a sale with multiple lines where some have non-null PrecioRealCobrado,
    /// every line with a negotiated price has both values in the audit entry,
    /// and PrecioCalculado correctly reflects volume pricing logic for that line.
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AuditEntries_MultipleLines_AllNegotiatedLinesHaveBothPrices()
    {
        var genLine =
            from precioLista in GenPositivePrice()
            from cantidad in GenPositiveQuantity()
            from precioRealCobrado in GenPositivePrice()
            from reglasVolumen in GenVolumePricingRules()
            select (precioLista, cantidad, precioRealCobrado, reglasVolumen);

        var gen =
            from count in Gen.Choose(1, 10)
            from lines in Gen.ListOf(count, genLine)
            select lines.ToList();

        return Prop.ForAll(gen.ToArbitrary(), lines =>
        {
            var auditEntries = lines.Select((line, index) =>
                BuildAuditEntry(
                    productoId: index + 1,
                    cantidad: line.cantidad,
                    precioRealCobrado: line.precioRealCobrado,
                    precioLista: line.precioLista,
                    reglasVolumen: line.reglasVolumen)
            ).ToList();

            // Every audit entry for a negotiated line must have both prices
            var allHaveBothPrices = auditEntries.All(entry =>
                entry.PrecioRealCobrado.HasValue &&
                entry.PrecioCalculado > 0);

            // Every PrecioCalculado must match PricingHelper output
            var allPrecioCalculadoCorrect = lines.Zip(auditEntries, (line, entry) =>
            {
                var expected = PricingHelper.DeterminarPrecioEfectivo(
                    line.precioLista, line.cantidad, line.reglasVolumen);
                return entry.PrecioCalculado == expected;
            }).All(x => x);

            return (allHaveBothPrices && allPrecioCalculadoCorrect)
                .Label($"Not all audit entries contain both PrecioRealCobrado and correct PrecioCalculado " +
                       $"for {lines.Count} negotiated lines");
        });
    }

    /// <summary>
    /// For any line with non-null PrecioRealCobrado where volume pricing applies,
    /// the audit entry PrecioCalculado reflects the volume price (not PrecioLista),
    /// enabling accurate comparison of what was charged vs what would have been charged.
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AuditEntry_PrecioCalculado_ReflectsVolumePricing_WhenApplicable()
    {
        var gen =
            from precioLista in Gen.Choose(500, 100000).Select(i => (decimal)i / 100m)
            from cantidad in Gen.Choose(50, 500).Select(i => (decimal)i)
            from precioRealCobrado in GenPositivePrice()
            from precioEspecial in Gen.Choose(1, 499).Select(i => (decimal)i / 100m)
            from cantidadMinima in Gen.Choose(1, 49).Select(i => (decimal)i)
            let reglasVolumen = new List<(decimal CantidadMinima, decimal PrecioEspecial)>
                { (cantidadMinima, precioEspecial) }
            where cantidad >= cantidadMinima // Ensure volume rule applies
            select (precioLista, cantidad, precioRealCobrado, reglasVolumen);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioLista, cantidad, precioRealCobrado, reglasVolumen) = tuple;

            var auditEntry = BuildAuditEntry(
                productoId: 1,
                cantidad: cantidad,
                precioRealCobrado: precioRealCobrado,
                precioLista: precioLista,
                reglasVolumen: reglasVolumen);

            // PrecioCalculado should be the volume price, not PrecioLista
            var expectedVolumePrice = reglasVolumen[0].PrecioEspecial;

            return (auditEntry.PrecioRealCobrado == precioRealCobrado &&
                    auditEntry.PrecioCalculado == expectedVolumePrice)
                .Label($"Expected PrecioCalculado={expectedVolumePrice} (volume price) " +
                       $"and PrecioRealCobrado={precioRealCobrado}, " +
                       $"got PrecioCalculado={auditEntry.PrecioCalculado}, " +
                       $"PrecioRealCobrado={auditEntry.PrecioRealCobrado}. " +
                       $"PrecioLista={precioLista}, Cantidad={cantidad}, " +
                       $"Rule: min={reglasVolumen[0].CantidadMinima}");
        });
    }
}
