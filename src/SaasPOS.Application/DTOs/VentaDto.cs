namespace SaasPOS.Application.DTOs;

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record CrearVentaRequest(
    int SucursalId,
    string? TipoComprobante,
    int? ClienteId,
    List<LineaVentaRequest> Lineas,
    string? MetodoPago = "Efectivo",
    int CuotasMeses = 0,
    string? ReferenciaTransaccion = null);

public record LineaVentaRequest(
    int ProductoId,
    decimal Cantidad,
    decimal? PrecioRealCobrado = null);

public record CrearVentaBarEscolarRequest(
    int SucursalId,
    int ProductoId);

// ── Response DTOs ────────────────────────────────────────────────────────────

public record VentaResponse(
    int Id,
    int ComercioId,
    int SucursalId,
    int UsuarioId,
    int? ClienteId,
    decimal Total,
    string TipoComprobante,
    string MetodoPago,
    int CuotasMeses,
    decimal? ValorCuota,
    string? ReferenciaTransaccion,
    DateTime FechaVenta,
    bool ImpresionAutomatica,
    bool MostrarBotonCliente,
    List<DetalleVentaDto> Detalles);

public record DetalleVentaDto(
    int Id,
    int ProductoId,
    string ProductoNombre,
    decimal Cantidad,
    decimal PrecioRealCobrado,
    decimal Subtotal);

public record VentaListDto(
    int Id,
    int SucursalId,
    int UsuarioId,
    int? ClienteId,
    decimal Total,
    string TipoComprobante,
    string? EstadoSRI,
    DateTime FechaVenta);
