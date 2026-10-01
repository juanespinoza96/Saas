using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

public class EmailProcessorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailProcessorBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public EmailProcessorBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailProcessorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailProcessorBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEmailsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing email queue.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("EmailProcessorBackgroundService stopped.");
    }

    private async Task ProcessPendingEmailsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var smtpClient = scope.ServiceProvider.GetRequiredService<ISmtpClient>();

        var pendingEmails = await dbContext.ColaCorreos
            .IgnoreQueryFilters()
            .Where(c => c.Estado == "Pendiente")
            .OrderBy(c => c.FechaCreacion)
            .Take(50)
            .ToListAsync(stoppingToken);

        foreach (var correo in pendingEmails)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                await smtpClient.SendAsync(correo.Destinatario, correo.Asunto, correo.CuerpoHtml);

                correo.Estado = "Enviado";
                correo.FechaEnvio = DateTime.UtcNow;

                _logger.LogInformation(
                    "Email sent successfully to {Destinatario} (Id: {Id})",
                    correo.Destinatario, correo.Id);
            }
            catch (Exception ex)
            {
                correo.Intentos++;
                correo.UltimoError = ex.Message;

                if (correo.Intentos >= correo.MaxIntentos)
                {
                    correo.Estado = "Fallido";
                    _logger.LogWarning(
                        "Email to {Destinatario} (Id: {Id}) marked as Fallido after {Intentos} attempts. Error: {Error}",
                        correo.Destinatario, correo.Id, correo.Intentos, ex.Message);
                }
                else
                {
                    _logger.LogWarning(
                        "Email to {Destinatario} (Id: {Id}) failed attempt {Intentos}/{MaxIntentos}. Error: {Error}",
                        correo.Destinatario, correo.Id, correo.Intentos, correo.MaxIntentos, ex.Message);
                }
            }
        }

        if (pendingEmails.Count > 0)
        {
            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
