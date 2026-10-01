using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Integration.Email;

/// <summary>
/// Integration tests for the email queue system (ColaCorreo).
/// Validates enqueue, successful send, failure handling, and content correctness.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Email")]
public class EmailQueueTests : IntegrationTestBase
{
    public EmailQueueTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    // ── 17.1: Enqueued email has Estado Pendiente ──────────────────────────────

    [DockerAvailableFact]
    public async Task EnqueueAsync_CreatesColaCorreo_WithEstadoPendiente()
    {
        // Arrange
        await using var context = CreateDbContext();
        var emailService = new EmailService(context);

        // Act
        await emailService.EnqueueAsync(
            comercioId: null,
            destinatario: "user@example.com",
            asunto: "Bienvenido",
            cuerpoHtml: "<p>Hola</p>");

        // Assert
        await using var verifyContext = CreateDbContext();
        var correo = await verifyContext.ColaCorreos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync();

        Assert.NotNull(correo);
        Assert.Equal("Pendiente", correo.Estado);
        Assert.Equal(0, correo.Intentos);
        Assert.Equal(3, correo.MaxIntentos);
        Assert.Null(correo.FechaEnvio);
    }

    // ── 17.2: Successful send changes to Estado Enviado with FechaEnvio ────────

    [DockerAvailableFact]
    public async Task ProcessPendingEmails_OnSuccess_SetsEstadoEnviado_WithFechaEnvio()
    {
        // Arrange: seed a pending email
        await using var seedContext = CreateDbContext();
        var correo = new ColaCorreo
        {
            ComercioId = null,
            Destinatario = "success@example.com",
            Asunto = "Test Enviado",
            CuerpoHtml = "<p>Contenido</p>",
            Intentos = 0,
            MaxIntentos = 3,
            Estado = "Pendiente",
            FechaCreacion = DateTime.UtcNow
        };
        seedContext.ColaCorreos.Add(correo);
        await seedContext.SaveChangesAsync();
        var correoId = correo.Id;

        var beforeProcess = DateTime.UtcNow;

        // Act: run one processing cycle with a successful mock SMTP
        var scopeFactory = CreateScopeFactory(smtpSucceeds: true);
        await RunOneProcessingCycle(scopeFactory);

        // Assert
        await using var verifyContext = CreateDbContext();
        var updated = await verifyContext.ColaCorreos
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == correoId);

        Assert.Equal("Enviado", updated.Estado);
        Assert.NotNull(updated.FechaEnvio);
        Assert.True(updated.FechaEnvio >= beforeProcess);
    }

    // ── 17.3: 3 consecutive failures mark Estado Fallido with UltimoError ──────

    [DockerAvailableFact]
    public async Task ProcessPendingEmails_After3Failures_SetsEstadoFallido_WithUltimoError()
    {
        // Arrange: seed a pending email with MaxIntentos = 3
        await using var seedContext = CreateDbContext();
        var correo = new ColaCorreo
        {
            ComercioId = null,
            Destinatario = "fail@example.com",
            Asunto = "Test Fallido",
            CuerpoHtml = "<p>Contenido fallido</p>",
            Intentos = 0,
            MaxIntentos = 3,
            Estado = "Pendiente",
            FechaCreacion = DateTime.UtcNow
        };
        seedContext.ColaCorreos.Add(correo);
        await seedContext.SaveChangesAsync();
        var correoId = correo.Id;

        var errorMessage = "SMTP connection refused";

        // Act: run 3 processing cycles, each failing
        for (int i = 0; i < 3; i++)
        {
            var scopeFactory = CreateScopeFactory(smtpSucceeds: false, errorMessage: errorMessage);
            await RunOneProcessingCycle(scopeFactory);
        }

        // Assert
        await using var verifyContext = CreateDbContext();
        var updated = await verifyContext.ColaCorreos
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == correoId);

        Assert.Equal("Fallido", updated.Estado);
        Assert.Equal(3, updated.Intentos);
        Assert.NotNull(updated.UltimoError);
        Assert.Contains(errorMessage, updated.UltimoError);
    }

    // ── 17.4: Email content includes correct destinatario, asunto, cuerpoHtml ──

    [DockerAvailableFact]
    public async Task EnqueueAsync_StoresCorrectContent_DestinatarioAsuntoCuerpoHtml()
    {
        // Arrange
        await using var context = CreateDbContext();
        var emailService = new EmailService(context);

        const string destinatario = "cliente@negocio.ec";
        const string asunto = "Factura Electrónica #001-001-000000123";
        const string cuerpoHtml = "<html><body><h1>Factura</h1><p>Total: $150.00</p></body></html>";

        // Act
        await emailService.EnqueueAsync(
            comercioId: null,
            destinatario: destinatario,
            asunto: asunto,
            cuerpoHtml: cuerpoHtml);

        // Assert
        await using var verifyContext = CreateDbContext();
        var correo = await verifyContext.ColaCorreos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync();

        Assert.NotNull(correo);
        Assert.Equal(destinatario, correo.Destinatario);
        Assert.Equal(asunto, correo.Asunto);
        Assert.Equal(cuerpoHtml, correo.CuerpoHtml);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an IServiceScopeFactory that resolves AppDbContext from the Testcontainer
    /// and a mock ISmtpClient configured to succeed or throw.
    /// </summary>
    private IServiceScopeFactory CreateScopeFactory(bool smtpSucceeds, string? errorMessage = null)
    {
        var smtpMock = new Mock<ISmtpClient>();
        if (smtpSucceeds)
        {
            smtpMock.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
        }
        else
        {
            smtpMock.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException(errorMessage ?? "SMTP Error"));
        }

        var dbContext = CreateDbContext();

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(AppDbContext)))
            .Returns(dbContext);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ISmtpClient)))
            .Returns(smtpMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return scopeFactoryMock.Object;
    }

    /// <summary>
    /// Runs one processing cycle of EmailProcessorBackgroundService.
    /// </summary>
    private static async Task RunOneProcessingCycle(IServiceScopeFactory scopeFactory)
    {
        var loggerMock = new Mock<ILogger<EmailProcessorBackgroundService>>();
        var service = new EmailProcessorBackgroundService(scopeFactory, loggerMock.Object);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }
}
