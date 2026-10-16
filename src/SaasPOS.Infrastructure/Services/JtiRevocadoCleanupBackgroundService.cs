using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Servicio en segundo plano que realiza el barrido periódico de la tabla de tokens JWT
/// revocados (JtiRevocados), eliminando las filas ya vencidas (Requirements 10.1–10.4):
/// - En cada ciclo borra las filas cuya Expiracion ya pasó (Expiracion menor que el instante
///   actual UTC), que son revocaciones sin efecto porque el token ya expiró por sí mismo
///   (auto-expiración lógica). Así la tabla se mantiene acotada en tamaño.
///
/// Sigue el patrón de los servicios en segundo plano existentes
/// (PasswordRecoveryCleanupBackgroundService, EmailProcessorBackgroundService y
/// BillingCutBackgroundService): usa IServiceScopeFactory para crear un scope efímero por
/// iteración y obtener un AppDbContext (Scoped) aislado, y un bucle
/// while (!stoppingToken.IsCancellationRequested) envuelto en try/catch que loguea el error
/// y continúa sin tumbar el servicio. Corre sin contexto de tenant, consistente con que
/// JtiRevocados es estado de seguridad global (sin HasQueryFilter de tenant).
/// </summary>
public class JtiRevocadoCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JtiRevocadoCleanupBackgroundService> _logger;

    // Intervalo del barrido: 30 minutos (dentro del rango permitido 15–30, Requirement 10.3).
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

    public JtiRevocadoCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<JtiRevocadoCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JtiRevocadoCleanupBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await LimpiarJtiVencidosAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Se loguea el error y se continúa: un fallo puntual del barrido no debe
                // tumbar el servicio ni detener futuros ciclos de limpieza.
                _logger.LogError(ex, "Error processing JTI revocation cleanup sweep.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("JtiRevocadoCleanupBackgroundService stopped.");
    }

    private async Task LimpiarJtiVencidosAsync(CancellationToken stoppingToken)
    {
        // Se crea un scope propio por iteración para obtener un AppDbContext (Scoped) aislado.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ahora = DateTime.UtcNow;

        // Borrado directo (ExecuteDeleteAsync) de todas las filas vencidas: tokens cuya
        // Expiracion ya pasó. No se cargan en memoria; se emite un único DELETE en servidor.
        var filasBorradas = await db.JtiRevocados
            .Where(j => j.Expiracion < ahora)
            .ExecuteDeleteAsync(stoppingToken);

        if (filasBorradas > 0)
        {
            _logger.LogInformation(
                "JTI revocation cleanup sweep: {FilasBorradas} jti revocados vencidos borrados.",
                filasBorradas);
        }
    }
}
