namespace SaasPOS.Domain.Entities;

public class Venta
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public int SucursalId { get; set; }
    public int UsuarioId { get; set; }
    public int? ClienteId { get; set; }
    public decimal Total { get; set; }
    /// <summary>Ticket Interno | Factura Electronica</summary>
    public string TipoComprobante { get; set; } = "Ticket Interno";
    /// <summary>Pendiente | Autorizada | Error</summary>
    public string? EstadoSRI { get; set; }
    /// <summary>Efectivo | TarjetaCredito | TarjetaDebito | Transferencia</summary>
    public string MetodoPago { get; set; } = "Efectivo";
    /// <summary>Number of installments (0 = cash/immediate payment)</summary>
    public int CuotasMeses { get; set; } = 0;
    /// <summary>Monthly installment value when CuotasMeses > 0</summary>
    public decimal? ValorCuota { get; set; }
    /// <summary>External transaction reference for card/transfer payments</summary>
    public string? ReferenciaTransaccion { get; set; }
    public DateTime FechaVenta { get; set; }

    // Navigation
    public Comercio Comercio { get; set; } = null!;
    public Sucursal Sucursal { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
    public Cliente? Cliente { get; set; }
    public ICollection<DetalleVenta> Detalles { get; set; } = [];
}
