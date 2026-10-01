using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _dbContext;
    private readonly IEmailService _emailService;

    public NotificationService(AppDbContext dbContext, IEmailService emailService)
    {
        _dbContext = dbContext;
        _emailService = emailService;
    }

    /// <summary>
    /// Req 14.1: Creates a low-stock notification when CantidadFisica falls below StockMinimo.
    /// </summary>
    public async Task CrearNotificacionStockBajoAsync(
        int comercioId, int sucursalId, int productoId, decimal cantidadActual)
    {
        var producto = await _dbContext.Productos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == productoId);

        var nombreProducto = producto?.Nombre ?? $"Producto #{productoId}";

        var notificacion = new Notificacion
        {
            ComercioId = comercioId,
            SucursalId = sucursalId,
            ProductoId = productoId,
            Titulo = "Stock bajo",
            Mensaje = $"El producto '{nombreProducto}' tiene stock bajo: {cantidadActual} unidades.",
            Leida = false,
            TipoNotificacion = "StockBajo",
            FechaEmision = DateTime.UtcNow
        };

        _dbContext.Notificaciones.Add(notificacion);
        await _dbContext.SaveChangesAsync();

        // Req 14.5: Also enqueue email to Gerente for low-stock notification
        var gerenteEmail = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.ComercioId == comercioId && u.Rol == "Gerente" && u.Activo)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();

        if (gerenteEmail != null)
        {
            await _emailService.EnqueueAsync(
                comercioId,
                gerenteEmail,
                "Alerta: Stock bajo",
                $"El producto '{nombreProducto}' en la sucursal #{sucursalId} tiene stock bajo: {cantidadActual} unidades. Por favor gestione el reabastecimiento.");
        }
    }

    /// <summary>
    /// Creates a notification for upcoming payment due date.
    /// </summary>
    public async Task CrearNotificacionPagoProximoAsync(int comercioId, decimal montoCuota, DateTime fechaCorte)
    {
        var notificacion = new Notificacion
        {
            ComercioId = comercioId,
            SucursalId = null,
            ProductoId = null,
            Titulo = "Pago próximo a vencer",
            Mensaje = $"Su cuota de ${montoCuota:F2} vence el {fechaCorte:dd/MM/yyyy}. Realice el pago para evitar la suspensión del servicio.",
            Leida = false,
            TipoNotificacion = "PagoProximo",
            FechaEmision = DateTime.UtcNow
        };

        _dbContext.Notificaciones.Add(notificacion);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a notification for overdue payment (mora).
    /// </summary>
    public async Task CrearNotificacionMoraAsync(int comercioId)
    {
        var notificacion = new Notificacion
        {
            ComercioId = comercioId,
            SucursalId = null,
            ProductoId = null,
            Titulo = "Pago en mora",
            Mensaje = "Su cuenta se encuentra en mora. Realice el pago a la brevedad para evitar la suspensión del servicio.",
            Leida = false,
            TipoNotificacion = "Mora",
            FechaEmision = DateTime.UtcNow
        };

        _dbContext.Notificaciones.Add(notificacion);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Req 14.2: Returns all unread notifications for a commerce.
    /// The EF Core global query filter on ComercioId provides base tenant isolation.
    /// </summary>
    public async Task<List<Notificacion>> GetNotificacionesPendientesAsync(int comercioId)
    {
        return await _dbContext.Notificaciones
            .IgnoreQueryFilters()
            .Where(n => n.ComercioId == comercioId && !n.Leida)
            .OrderByDescending(n => n.FechaEmision)
            .ToListAsync();
    }

    /// <summary>
    /// Req 14.3, 14.4: Marks a notification as read, verifying ComercioId for cross-tenant isolation.
    /// </summary>
    public async Task MarcarLeidaAsync(int notificacionId, int comercioId)
    {
        var notificacion = await _dbContext.Notificaciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == notificacionId && n.ComercioId == comercioId);

        if (notificacion is null)
            throw new InvalidOperationException("Notificación no encontrada o no pertenece al comercio.");

        notificacion.Leida = true;
        await _dbContext.SaveChangesAsync();
    }
}
