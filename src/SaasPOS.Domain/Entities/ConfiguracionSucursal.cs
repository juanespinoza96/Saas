namespace SaasPOS.Domain.Entities;

public class ConfiguracionSucursal
{
    public int SucursalId { get; set; }
    public bool EsBarEscolar { get; set; }
    public bool MostrarBotonCliente { get; set; }
    public bool PermiteVentaEnNegativo { get; set; }
    public bool ImpresionAutomaticaTicket { get; set; }
    public bool PermitePrecioNegociado { get; set; }

    /// <summary>
    /// Controla si los Cajeros pueden ver los totales de ventas diarias y el historial de ventas.
    /// </summary>
    public bool MostrarVentasAlCajero { get; set; } = false;

    // Navigation
    public Sucursal Sucursal { get; set; } = null!;
}
