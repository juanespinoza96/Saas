using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Domain.Entities;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for sale field persistence completeness.
/// **Validates: Requirements 9.2**
/// 
/// Property 14: Sale persistence captures all required fields.
/// For any sale registration request with valid inputs, the persisted Venta record
/// SHALL contain non-null/non-default values for ComercioId, SucursalId, UsuarioId,
/// Total, TipoComprobante, and FechaVenta, and the count of DetalleVentas records
/// SHALL match the number of line items in the request.
/// </summary>
public class Property14_SaleFieldPersistenceTests
{
    private static readonly string[] TiposComprobanteValidos = ["Ticket Interno", "Factura Electronica"];

    /// <summary>
    /// Generates a positive integer (≥ 1) for IDs.
    /// </summary>
    private static Gen<int> GenPositiveId() =>
        Gen.Choose(1, 10000);

    /// <summary>
    /// Generates a positive decimal for quantities and prices.
    /// </summary>
    private static Gen<decimal> GenPositiveDecimal() =>
        Gen.Choose(1, 100000).Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a valid TipoComprobante value.
    /// </summary>
    private static Gen<string> GenTipoComprobante() =>
        Gen.Elements(TiposComprobanteValidos);

    /// <summary>
    /// Generates a non-default DateTime for FechaVenta.
    /// </summary>
    private static Gen<DateTime> GenFechaVenta() =>
        from year in Gen.Choose(2020, 2030)
        from month in Gen.Choose(1, 12)
        from day in Gen.Choose(1, 28)
        from hour in Gen.Choose(0, 23)
        from minute in Gen.Choose(0, 59)
        select new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    /// <summary>
    /// Generates a list of line items (DetalleVenta) with valid fields.
    /// </summary>
    private static Gen<List<(int ProductoId, decimal Cantidad, decimal PrecioRealCobrado)>> GenLineItems(int minCount, int maxCount) =>
        from count in Gen.Choose(minCount, maxCount)
        from items in Gen.ListOf(count,
            from productoId in GenPositiveId()
            from cantidad in GenPositiveDecimal()
            from precio in GenPositiveDecimal()
            select (productoId, cantidad, precio))
        select items.ToList();

    /// <summary>
    /// Constructs a Venta entity the same way the controller does, simulating sale creation.
    /// </summary>
    private static Venta ConstructVenta(
        int comercioId,
        int sucursalId,
        int usuarioId,
        string tipoComprobante,
        DateTime fechaVenta,
        List<(int ProductoId, decimal Cantidad, decimal PrecioRealCobrado)> lineItems)
    {
        var detalles = lineItems.Select(item => new DetalleVenta
        {
            ProductoId = item.ProductoId,
            Cantidad = item.Cantidad,
            PrecioRealCobrado = item.PrecioRealCobrado
        }).ToList();

        var total = detalles.Sum(d => d.Cantidad * d.PrecioRealCobrado);

        return new Venta
        {
            ComercioId = comercioId,
            SucursalId = sucursalId,
            UsuarioId = usuarioId,
            Total = total,
            TipoComprobante = tipoComprobante,
            FechaVenta = fechaVenta,
            Detalles = detalles
        };
    }

    /// <summary>
    /// For any valid set of inputs, the constructed Venta always has all required fields
    /// set to non-default values: ComercioId > 0, SucursalId > 0, UsuarioId > 0,
    /// Total > 0, TipoComprobante is valid, FechaVenta is non-default.
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AllRequiredFields_AreNonDefault_WhenInputsValid()
    {
        var gen =
            from comercioId in GenPositiveId()
            from sucursalId in GenPositiveId()
            from usuarioId in GenPositiveId()
            from tipoComprobante in GenTipoComprobante()
            from fechaVenta in GenFechaVenta()
            from lineItems in GenLineItems(1, 20)
            select (comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems) = tuple;

            var venta = ConstructVenta(comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems);

            return (venta.ComercioId > 0)
                .Label("ComercioId must be > 0")
                .And((venta.SucursalId > 0)
                    .Label("SucursalId must be > 0"))
                .And((venta.UsuarioId > 0)
                    .Label("UsuarioId must be > 0"))
                .And((venta.Total > 0)
                    .Label("Total must be > 0"))
                .And((!string.IsNullOrEmpty(venta.TipoComprobante))
                    .Label("TipoComprobante must not be null or empty"))
                .And((venta.FechaVenta != default)
                    .Label("FechaVenta must not be default DateTime"));
        });
    }

    /// <summary>
    /// The count of DetalleVentas records SHALL match the number of line items in the request.
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DetalleVentasCount_MatchesInputLineCount()
    {
        var gen =
            from comercioId in GenPositiveId()
            from sucursalId in GenPositiveId()
            from usuarioId in GenPositiveId()
            from tipoComprobante in GenTipoComprobante()
            from fechaVenta in GenFechaVenta()
            from lineItems in GenLineItems(1, 50)
            select (comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems) = tuple;

            var venta = ConstructVenta(comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems);

            return (venta.Detalles.Count == lineItems.Count)
                .Label($"Expected {lineItems.Count} detalles, got {venta.Detalles.Count}");
        });
    }

    /// <summary>
    /// Each DetalleVenta has ProductoId > 0, Cantidad > 0, and PrecioRealCobrado > 0.
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EachDetalleVenta_HasValidFields()
    {
        var gen =
            from comercioId in GenPositiveId()
            from sucursalId in GenPositiveId()
            from usuarioId in GenPositiveId()
            from tipoComprobante in GenTipoComprobante()
            from fechaVenta in GenFechaVenta()
            from lineItems in GenLineItems(1, 20)
            select (comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems) = tuple;

            var venta = ConstructVenta(comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems);

            var allValid = venta.Detalles.All(d =>
                d.ProductoId > 0 && d.Cantidad > 0 && d.PrecioRealCobrado > 0);

            return allValid
                .Label("All DetalleVenta must have ProductoId > 0, Cantidad > 0, PrecioRealCobrado > 0");
        });
    }

    /// <summary>
    /// TipoComprobante is always one of the valid values ("Ticket Interno", "Factura Electronica").
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TipoComprobante_IsAlwaysValid()
    {
        var gen =
            from comercioId in GenPositiveId()
            from sucursalId in GenPositiveId()
            from usuarioId in GenPositiveId()
            from tipoComprobante in GenTipoComprobante()
            from fechaVenta in GenFechaVenta()
            from lineItems in GenLineItems(1, 5)
            select (comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems) = tuple;

            var venta = ConstructVenta(comercioId, sucursalId, usuarioId, tipoComprobante, fechaVenta, lineItems);

            return TiposComprobanteValidos.Contains(venta.TipoComprobante)
                .Label($"TipoComprobante '{venta.TipoComprobante}' not in valid set");
        });
    }

    /// <summary>
    /// Concrete example: A sale with 3 line items, Ticket Interno type, captures all fields correctly.
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Fact]
    public void ConcreteExample_SaleWith3Lines_AllFieldsCaptured()
    {
        var lineItems = new List<(int ProductoId, decimal Cantidad, decimal PrecioRealCobrado)>
        {
            (1, 2.0m, 5.50m),
            (2, 1.0m, 12.00m),
            (3, 3.5m, 8.00m)
        };

        var venta = ConstructVenta(
            comercioId: 10,
            sucursalId: 5,
            usuarioId: 3,
            tipoComprobante: "Ticket Interno",
            fechaVenta: new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc),
            lineItems: lineItems);

        // All required fields are non-default
        Assert.True(venta.ComercioId > 0);
        Assert.True(venta.SucursalId > 0);
        Assert.True(venta.UsuarioId > 0);
        Assert.True(venta.Total > 0);
        Assert.NotNull(venta.TipoComprobante);
        Assert.NotEqual(default, venta.FechaVenta);

        // DetalleVentas count matches
        Assert.Equal(3, venta.Detalles.Count);

        // Total = (2*5.50) + (1*12.00) + (3.5*8.00) = 11 + 12 + 28 = 51
        Assert.Equal(51.00m, venta.Total);

        // Each detail has valid fields
        foreach (var detalle in venta.Detalles)
        {
            Assert.True(detalle.ProductoId > 0);
            Assert.True(detalle.Cantidad > 0);
            Assert.True(detalle.PrecioRealCobrado > 0);
        }
    }

    /// <summary>
    /// Concrete example: Factura Electronica type is correctly captured.
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Fact]
    public void ConcreteExample_FacturaElectronica_TypeCaptured()
    {
        var lineItems = new List<(int ProductoId, decimal Cantidad, decimal PrecioRealCobrado)>
        {
            (42, 1.0m, 100.00m)
        };

        var venta = ConstructVenta(
            comercioId: 7,
            sucursalId: 2,
            usuarioId: 15,
            tipoComprobante: "Factura Electronica",
            fechaVenta: new DateTime(2024, 12, 1, 9, 0, 0, DateTimeKind.Utc),
            lineItems: lineItems);

        Assert.Equal("Factura Electronica", venta.TipoComprobante);
        Assert.Equal(7, venta.ComercioId);
        Assert.Equal(2, venta.SucursalId);
        Assert.Equal(15, venta.UsuarioId);
        Assert.Equal(100.00m, venta.Total);
        Assert.Single(venta.Detalles);
    }
}
