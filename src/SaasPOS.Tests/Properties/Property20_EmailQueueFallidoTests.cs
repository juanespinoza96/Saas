using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for email queue reaching Fallido state after MaxIntentos failures.
/// **Validates: Requirements 19.12**
/// </summary>
public class Property20_EmailQueueFallidoTests
{
    /// <summary>
    /// Creates an InMemory AppDbContext with tenant context configured as SuperAdmin.
    /// </summary>
    private static AppDbContext CreateDbContext(string dbName)
    {
        var tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.ComercioId).Returns(1);
        tenantMock.Setup(t => t.IsSuperAdmin).Returns(true);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, tenantMock.Object);
    }

    /// <summary>
    /// Creates an IServiceScopeFactory mock that returns a scope providing the given DbContext
    /// and a mock ISmtpClient that always throws the specified exception.
    /// </summary>
    private static IServiceScopeFactory CreateScopeFactory(AppDbContext dbContext, string errorMessage)
    {
        var smtpClientMock = new Mock<ISmtpClient>();
        smtpClientMock
            .Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException(errorMessage));

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(AppDbContext)))
            .Returns(dbContext);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ISmtpClient)))
            .Returns(smtpClientMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return scopeFactoryMock.Object;
    }

    /// <summary>
    /// Simulates one processing cycle of the EmailProcessorBackgroundService by starting it
    /// and immediately cancelling after the first processing pass completes.
    /// </summary>
    private static async Task RunOneProcessingCycle(IServiceScopeFactory scopeFactory)
    {
        var loggerMock = new Mock<ILogger<EmailProcessorBackgroundService>>();
        var service = new EmailProcessorBackgroundService(scopeFactory, loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Start the service - it will process pending emails immediately before the delay
        await service.StartAsync(cts.Token);

        // Give it a moment to process the first batch
        await Task.Delay(100);

        // Stop the service
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Property 20: For any email in the queue with MaxIntentos = N (between 1 and 10),
    /// after exactly N consecutive SMTP failures (each incrementing Intentos by 1),
    /// the email's Estado SHALL be 'Fallido' and UltimoError SHALL contain the error
    /// message from the last failed attempt.
    /// **Validates: Requirements 19.12**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool EmailQueue_ReachesFallido_AfterMaxIntentosFailures(PositiveInt maxIntentosRaw)
    {
        // Constrain MaxIntentos to a reasonable range (1-10)
        var maxIntentos = (maxIntentosRaw.Get % 10) + 1;
        var dbName = Guid.NewGuid().ToString();
        var errorMessage = $"SMTP connection failed - test error {Guid.NewGuid()}";

        using var db = CreateDbContext(dbName);

        // Seed required base data
        db.Planes.Add(new Plan { Id = 1, Nombre = "Básico", Precio = 10m, LimiteUsuarios = 5, LimiteAtributos = 2 });
        db.Comercios.Add(new Comercio { Id = 1, Ruc = "0990000001001", RazonSocial = "Test Commerce", PlanId = 1, FechaRegistro = DateTime.UtcNow });
        db.SaveChanges();

        // Seed a ColaCorreo record with Estado = "Pendiente", Intentos = 0, MaxIntentos = N
        var correo = new ColaCorreo
        {
            Id = 1,
            ComercioId = 1,
            Destinatario = "test@example.com",
            Asunto = "Test Subject",
            CuerpoHtml = "<p>Test Body</p>",
            Intentos = 0,
            MaxIntentos = maxIntentos,
            Estado = "Pendiente",
            FechaCreacion = DateTime.UtcNow
        };
        db.ColaCorreos.Add(correo);
        db.SaveChanges();

        var scopeFactory = CreateScopeFactory(db, errorMessage);

        // Run the processing cycle MaxIntentos times to exhaust retries
        for (int i = 0; i < maxIntentos; i++)
        {
            RunOneProcessingCycle(scopeFactory).GetAwaiter().GetResult();
        }

        // Reload the entity from the context
        var updatedCorreo = db.ColaCorreos
            .IgnoreQueryFilters()
            .First(c => c.Id == 1);

        // After MaxIntentos failures:
        // 1. Estado must be "Fallido"
        // 2. UltimoError must contain the error message
        // 3. Intentos must equal MaxIntentos
        return updatedCorreo.Estado == "Fallido"
            && updatedCorreo.UltimoError != null
            && updatedCorreo.UltimoError.Contains("SMTP connection failed")
            && updatedCorreo.Intentos == maxIntentos;
    }
}
