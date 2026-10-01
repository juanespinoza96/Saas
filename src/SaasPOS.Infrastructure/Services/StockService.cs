using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

public class StockService : IStockService
{
    private readonly AppDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IAuditService _auditService;
    private readonly ILogger<StockService> _logger;

    public StockService(
        AppDbContext dbContext,
        INotificationService notificationService,
        IAuditService auditService,
        ILogger<StockService> logger)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<StockResult> DecrementarStockVentaDirectaAsync(
        int productoId, int sucursalId, decimal cantidad, int comercioId)
    {
        var config = await _dbContext.ConfiguracionesComercio
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ComercioId == comercioId);

        var permiteNegativo = config?.PermiteVentaEnNegativo ?? false;

        var stock = await GetOrCreateStockAsync(productoId, sucursalId);

        if (!permiteNegativo && (stock.CantidadFisica - cantidad) < 0)
        {
            return new StockResult(false, "Stock insuficiente");
        }

        stock.CantidadFisica -= cantidad;
        await _dbContext.SaveChangesAsync();

        if (stock.CantidadFisica < stock.StockMinimo)
        {
            await _notificationService.CrearNotificacionStockBajoAsync(
                comercioId, sucursalId, productoId, stock.CantidadFisica);
        }

        return new StockResult(true);
    }

    public async Task<StockResult> DecrementarStockEnsambladoAsync(
        int productoEnsambladoId, int sucursalId, decimal cantidad, int comercioId)
    {
        var recetas = await _dbContext.RecetasProducto
            .IgnoreQueryFilters()
            .Where(r => r.ProductoFinalId == productoEnsambladoId)
            .ToListAsync();

        if (recetas.Count == 0)
        {
            return new StockResult(false, "No recipe defined");
        }

        var config = await _dbContext.ConfiguracionesComercio
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ComercioId == comercioId);

        var permiteNegativo = config?.PermiteVentaEnNegativo ?? false;

        // Pre-validate all ingredients have sufficient stock
        if (!permiteNegativo)
        {
            foreach (var receta in recetas)
            {
                var cantidadRequerida = receta.CantidadRequerida * cantidad;
                var stock = await GetOrCreateStockAsync(receta.IngredienteId, sucursalId);

                if ((stock.CantidadFisica - cantidadRequerida) < 0)
                {
                    return new StockResult(false, "Stock insuficiente");
                }
            }
        }

        // Decrement all ingredients in a single transaction
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            foreach (var receta in recetas)
            {
                var cantidadRequerida = receta.CantidadRequerida * cantidad;
                var stock = await GetOrCreateStockAsync(receta.IngredienteId, sucursalId);

                stock.CantidadFisica -= cantidadRequerida;

                if (stock.CantidadFisica < stock.StockMinimo)
                {
                    await _notificationService.CrearNotificacionStockBajoAsync(
                        comercioId, sucursalId, receta.IngredienteId, stock.CantidadFisica);
                }
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error decrementing stock for ensamblado ProductoId={ProductoId}", productoEnsambladoId);
            return new StockResult(false, "Error al actualizar stock");
        }

        return new StockResult(true);
    }

    public async Task<decimal> GetCantidadFisicaAsync(int productoId, int sucursalId)
    {
        var stock = await _dbContext.StockSucursal
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.ProductoId == productoId && s.SucursalId == sucursalId);

        return stock?.CantidadFisica ?? 0;
    }

    public async Task AjustarStockAsync(int productoId, int sucursalId, decimal nuevaCantidad, int usuarioId, int comercioId)
    {
        var stock = await GetOrCreateStockAsync(productoId, sucursalId);
        var cantidadAnterior = stock.CantidadFisica;
        stock.CantidadFisica = nuevaCantidad;
        await _dbContext.SaveChangesAsync();

        // Req 15.1: Audit log for stock adjustment
        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "AjustarStock",
            "StockSucursal",
            stock.Id.ToString(),
            new { ProductoId = productoId, SucursalId = sucursalId, CantidadFisica = cantidadAnterior },
            new { ProductoId = productoId, SucursalId = sucursalId, CantidadFisica = nuevaCantidad });
    }

    public async Task<bool> ValidarStockDisponibleAsync(
        int productoId, int sucursalId, decimal cantidadRequerida, bool permiteNegativo)
    {
        if (permiteNegativo)
            return true;

        var stock = await _dbContext.StockSucursal
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.ProductoId == productoId && s.SucursalId == sucursalId);

        var cantidadActual = stock?.CantidadFisica ?? 0;
        return (cantidadActual - cantidadRequerida) >= 0;
    }

    private async Task<StockSucursal> GetOrCreateStockAsync(int productoId, int sucursalId)
    {
        var stock = await _dbContext.StockSucursal
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.ProductoId == productoId && s.SucursalId == sucursalId);

        if (stock is null)
        {
            stock = new StockSucursal
            {
                ProductoId = productoId,
                SucursalId = sucursalId,
                CantidadFisica = 0,
                StockMinimo = 0
            };
            _dbContext.StockSucursal.Add(stock);
            await _dbContext.SaveChangesAsync();
        }

        return stock;
    }
}
