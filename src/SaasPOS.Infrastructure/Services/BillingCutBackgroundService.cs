using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Background service that runs daily at midnight to process billing cuts:
/// - 7 days before corte → "Por vencer" notification + email (Req 19.4)
/// - Day of corte without payment → "En mora" notification + email (Req 19.5)
/// - 3 days after mora without payment → suspend Comercio (Req 19.6)
/// </summary>
public class BillingCutBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingCutBackgroundService> _logger;

    public BillingCutBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<BillingCutBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BillingCutBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();
                await billingService.ProcesarCortesDiariosAsync(DateTime.UtcNow);
                _logger.LogInformation("Daily billing cut processed at {Time}", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing daily billing cut.");
            }

            // Wait until next day (calculate time until midnight UTC)
            var now = DateTime.UtcNow;
            var nextMidnight = now.Date.AddDays(1);
            var delay = nextMidnight - now;
            await Task.Delay(delay, stoppingToken);
        }

        _logger.LogInformation("BillingCutBackgroundService stopped.");
    }
}
