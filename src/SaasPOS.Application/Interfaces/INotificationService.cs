using SaasPOS.Domain.Entities;

namespace SaasPOS.Application.Interfaces;

public interface INotificationService
{
    Task CrearNotificacionStockBajoAsync(int comercioId, int sucursalId, int productoId, decimal cantidadActual);
    Task CrearNotificacionPagoProximoAsync(int comercioId, decimal montoCuota, DateTime fechaCorte);
    Task CrearNotificacionMoraAsync(int comercioId);
    Task<List<Notificacion>> GetNotificacionesPendientesAsync(int comercioId);
    Task MarcarLeidaAsync(int notificacionId, int comercioId);
}
