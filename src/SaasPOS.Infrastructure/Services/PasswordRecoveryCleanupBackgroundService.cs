using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Servicio en segundo plano que realiza el barrido periódico de la recuperación de
/// contraseña jerárquica (Requirements 6.2, 6.3, 7.1, 7.2):
/// - Borra la contraseña temporal cifrada (PasswordTemporalCifrada = null) de las
///   solicitudes en estado "Aprobada" cuya FechaExpiracion ya venció (temporal vencida).
/// - Detecta las solicitudes en estado "Pendiente" cuya FechaSolicitud supera las 24 horas
///   y las trata como Expiradas.
///
/// Nota de diseño: el estado "Expirada" es un valor DERIVADO por validación perezosa; no
/// se persiste como estado propio de la solicitud (el Estado sigue siendo "Pendiente" hasta
/// que otra operación en vivo lo evalúe). Por eso, para las pendientes vencidas este barrido
/// solo registra su detección; la limpieza concreta que persiste es el borrado del cifrado
/// de las temporales vencidas, complementado con la validación perezosa en cada consulta/uso.
///
/// Sigue el patrón de los servicios en segundo plano existentes
/// (EmailProcessorBackgroundService y BillingCutBackgroundService): usa IServiceScopeFactory
/// para crear un scope por iteración y un bucle while (!stoppingToken.IsCancellationRequested)
/// con un intervalo fijo. Corre sin contexto de tenant, por lo que las consultas EF Core usan
/// IgnoreQueryFilters() ya que una SolicitudRecuperacion puede pertenecer a cualquier comercio.
/// </summary>
public class PasswordRecoveryCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PasswordRecoveryCleanupBackgroundService> _logger;

    // Intervalo del barrido: 15 minutos (Requirement 7.1).
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(15);

    // Plazo de expiración de una solicitud Pendiente sin resolver (Requirement 6.1).
    private static readonly TimeSpan PlazoExpiracionPendiente = TimeSpan.FromHours(24);

    public PasswordRecoveryCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PasswordRecoveryCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PasswordRecoveryCleanupBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await LimpiarSolicitudesVencidasAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing password recovery cleanup sweep.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("PasswordRecoveryCleanupBackgroundService stopped.");
    }

    private async Task LimpiarSolicitudesVencidasAsync(CancellationToken stoppingToken)
    {
        // Se crea un scope propio por iteración para obtener un AppDbContext (Scoped) aislado.
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ahora = DateTime.UtcNow;

        // ── Contraseñas temporales vencidas (Requirements 6.2, 6.3, 7.2) ──
        // Solicitudes Aprobadas con una temporal cifrada todavía almacenada cuya
        // FechaExpiracion ya pasó: se borra el valor cifrado (se pone a null).
        // Se usa IgnoreQueryFilters() porque el servicio corre sin contexto de tenant.
        var temporalesVencidas = await dbContext.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .Where(s => s.Estado == "Aprobada"
                        && s.PasswordTemporalCifrada != null
                        && s.FechaExpiracion != null
                        && s.FechaExpiracion < ahora)
            .ToListAsync(stoppingToken);

        foreach (var solicitud in temporalesVencidas)
        {
            // Borrar el cifrado de la temporal vencida para cerrar la ventana de riesgo.
            solicitud.PasswordTemporalCifrada = null;
        }

        // ── Solicitudes Pendientes vencidas (Requirements 6.1, 7.1) ──
        // Se detectan las Pendientes cuya FechaSolicitud supera las 24 horas para tratarlas
        // como Expiradas. El estado "Expirada" es derivado (validación perezosa), por lo que
        // aquí solo se registra su detección sin persistir un cambio de estado.
        var limitePendiente = ahora - PlazoExpiracionPendiente;
        var pendientesVencidas = await dbContext.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .Where(s => s.Estado == "Pendiente" && s.FechaSolicitud < limitePendiente)
            .CountAsync(stoppingToken);

        // Solo se persiste el borrado de las temporales vencidas.
        if (temporalesVencidas.Count > 0)
        {
            await dbContext.SaveChangesAsync(stoppingToken);
        }

        if (temporalesVencidas.Count > 0 || pendientesVencidas > 0)
        {
            _logger.LogInformation(
                "Password recovery cleanup sweep: {TemporalesBorradas} temporales vencidas borradas, {PendientesVencidas} solicitudes pendientes vencidas detectadas.",
                temporalesVencidas.Count, pendientesVencidas);
        }
    }
}
