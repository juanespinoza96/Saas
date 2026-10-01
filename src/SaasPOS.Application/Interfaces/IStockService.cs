namespace SaasPOS.Application.Interfaces;

public record StockResult(bool Success, string? Error = null);

public interface IStockService
{
    Task<StockResult> DecrementarStockVentaDirectaAsync(int productoId, int sucursalId, decimal cantidad, int comercioId);
    Task<StockResult> DecrementarStockEnsambladoAsync(int productoEnsambladoId, int sucursalId, decimal cantidad, int comercioId);
    Task<decimal> GetCantidadFisicaAsync(int productoId, int sucursalId);
    Task AjustarStockAsync(int productoId, int sucursalId, decimal nuevaCantidad, int usuarioId, int comercioId);
    Task<bool> ValidarStockDisponibleAsync(int productoId, int sucursalId, decimal cantidadRequerida, bool permiteNegativo);
}
