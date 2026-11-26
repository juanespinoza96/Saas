using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Verifica los destinatarios de los correos de cobro ("por vencer" y "mora"):
/// el Dueño activo y, si existe, también el Gerente activo, sin duplicados.
/// </summary>
public class BillingCobroDestinatariosTests
{
    private static (AppDbContext db, BillingService service, Mock<IEmailService> emailMock, Mock<INotificationService> notifMock)
        CreateContext()
    {
        var tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options, tenantMock.Object);

        var emailMock = new Mock<IEmailService>();
        emailMock
            .Setup(e => e.EnqueueAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var notifMock = new Mock<INotificationService>();

        var service = new BillingService(
            db,
            new Mock<IAuditService>().Object,
            notifMock.Object,
            emailMock.Object,
            new Mock<IJtiBlocklist>().Object,
            new Mock<ILogger<BillingService>>().Object);

        return (db, service, emailMock, notifMock);
    }

    /// <summary>
    /// Siembra un comercio con una suscripción "Activa" cuyo corte cae a los días indicados desde hoy
    /// (7 = por vencer, 0 = mora) y los usuarios pedidos.
    /// </summary>
    private static void Seed(AppDbContext db, int diasHastaCorte, string? duenoEmail, string? gerenteEmail,
        bool gerenteActivo = true)
    {
        db.Planes.Add(new Plan { Id = 1, Nombre = "Básico", Precio = 350m, LimiteUsuarios = 2, LimiteAtributos = 2, LimiteSucursales = 1 });
        var comercio = new Comercio
        {
            Ruc = "1234567890123",
            RazonSocial = "Comercio Cobro",
            PlanId = 1,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow.AddDays(-60)
        };
        db.Comercios.Add(comercio);
        db.SaveChanges();

        if (duenoEmail != null)
            db.Usuarios.Add(new Usuario { ComercioId = comercio.Id, Nombre = "Dueño", Email = duenoEmail, Rol = "Dueño", Activo = true, PasswordHash = "hash_placeholder" });
        if (gerenteEmail != null)
            db.Usuarios.Add(new Usuario { ComercioId = comercio.Id, Nombre = "Gerente", Email = gerenteEmail, Rol = "Gerente", Activo = gerenteActivo, PasswordHash = "hash_placeholder" });

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Suscripciones.Add(new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = 1,
            FechaInicio = hoy.AddDays(-30),
            FechaProximoCorte = hoy.AddDays(diasHastaCorte),
            MontoCuota = 350m,
            EsProporcional = false,
            Estado = "Activa"
        });
        db.SaveChanges();
    }

    private static void VerificarDestinatarios(Mock<IEmailService> emailMock, string asuntoParcial, params string[] esperados)
    {
        foreach (var email in esperados)
        {
            emailMock.Verify(e => e.EnqueueAsync(
                It.IsAny<int?>(), email,
                It.Is<string>(a => a.Contains(asuntoParcial, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<string>()), Times.Once);
        }
        emailMock.Verify(e => e.EnqueueAsync(
            It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(esperados.Length));
    }

    [Fact]
    public async Task PorVencer_ComercioBasicoSoloDueno_EncolaCorreoAlDueno()
    {
        var (db, service, emailMock, notifMock) = CreateContext();
        Seed(db, diasHastaCorte: 7, duenoEmail: "dueno@test.com", gerenteEmail: null);

        await service.ProcesarCortesDiariosAsync(DateTime.UtcNow);

        VerificarDestinatarios(emailMock, "vencer", "dueno@test.com");
        notifMock.Verify(n => n.CrearNotificacionPagoProximoAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Mora_ComercioBasicoSoloDueno_EncolaCorreoAlDueno()
    {
        var (db, service, emailMock, notifMock) = CreateContext();
        Seed(db, diasHastaCorte: 0, duenoEmail: "dueno@test.com", gerenteEmail: null);

        await service.ProcesarCortesDiariosAsync(DateTime.UtcNow);

        VerificarDestinatarios(emailMock, "mora", "dueno@test.com");
        notifMock.Verify(n => n.CrearNotificacionMoraAsync(It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task PorVencer_DuenoYGerente_EncolaDosCorreos()
    {
        var (db, service, emailMock, _) = CreateContext();
        Seed(db, diasHastaCorte: 7, duenoEmail: "dueno@test.com", gerenteEmail: "gerente@test.com");

        await service.ProcesarCortesDiariosAsync(DateTime.UtcNow);

        VerificarDestinatarios(emailMock, "vencer", "dueno@test.com", "gerente@test.com");
    }

    [Fact]
    public async Task Mora_DuenoYGerente_EncolaDosCorreos()
    {
        var (db, service, emailMock, _) = CreateContext();
        Seed(db, diasHastaCorte: 0, duenoEmail: "dueno@test.com", gerenteEmail: "gerente@test.com");

        await service.ProcesarCortesDiariosAsync(DateTime.UtcNow);

        VerificarDestinatarios(emailMock, "mora", "dueno@test.com", "gerente@test.com");
    }

    [Fact]
    public async Task Mora_DuenoYGerenteConMismoCorreo_EncolaUnSoloCorreo()
    {
        var (db, service, emailMock, _) = CreateContext();
        Seed(db, diasHastaCorte: 0, duenoEmail: "mismo@test.com", gerenteEmail: "MISMO@test.com");

        await service.ProcesarCortesDiariosAsync(DateTime.UtcNow);

        VerificarDestinatarios(emailMock, "mora", "mismo@test.com");
    }

    [Fact]
    public async Task Mora_GerenteInactivo_SoloRecibeElDueno()
    {
        var (db, service, emailMock, _) = CreateContext();
        Seed(db, diasHastaCorte: 0, duenoEmail: "dueno@test.com", gerenteEmail: "gerente@test.com", gerenteActivo: false);

        await service.ProcesarCortesDiariosAsync(DateTime.UtcNow);

        VerificarDestinatarios(emailMock, "mora", "dueno@test.com");
    }

    [Fact]
    public async Task PorVencer_SinDuenoNiGerente_NoEncolaCorreoPeroCreaNotificacion()
    {
        var (db, service, emailMock, notifMock) = CreateContext();
        Seed(db, diasHastaCorte: 7, duenoEmail: null, gerenteEmail: null);

        await service.ProcesarCortesDiariosAsync(DateTime.UtcNow);

        emailMock.Verify(e => e.EnqueueAsync(
            It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        notifMock.Verify(n => n.CrearNotificacionPagoProximoAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Mora_SinDuenoNiGerente_NoEncolaCorreoPeroCreaNotificacion()
    {
        var (db, service, emailMock, notifMock) = CreateContext();
        Seed(db, diasHastaCorte: 0, duenoEmail: null, gerenteEmail: null);

        await service.ProcesarCortesDiariosAsync(DateTime.UtcNow);

        emailMock.Verify(e => e.EnqueueAsync(
            It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        notifMock.Verify(n => n.CrearNotificacionMoraAsync(It.IsAny<int>()), Times.Once);
    }
}
