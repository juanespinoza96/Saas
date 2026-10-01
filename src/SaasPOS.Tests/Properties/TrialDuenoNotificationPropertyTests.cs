using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

// Feature: prueba-gratuita, Property 17: Notificaciones de trial al Dueño

/// <summary>
/// Property-based tests para verificar que las notificaciones de trial
/// se envían al usuario con rol Dueño (no al Gerente).
/// **Validates: Requirements 9.5**
///
/// Property 17: Notificaciones de trial al Dueño.
/// "Para cualquier Suscripción con Estado 'Trial' procesada por el BillingService
/// que genera notificación con email, el destinatario del correo SHALL ser
/// el usuario con rol 'Dueño' del Comercio (no el Gerente)."
/// </summary>
public class TrialDuenoNotificationPropertyTests
{
    private static readonly Mock<IAuditService> AuditMock = new();
    private static readonly Mock<INotificationService> NotificationMock = new();
    private static readonly Mock<IJtiBlocklist> JtiMock = new();
    private static readonly Mock<ILogger<BillingService>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto de base de datos InMemory con un BillingService configurado
    /// y un mock de IEmailService para capturar llamadas.
    /// </summary>
    private static (AppDbContext db, BillingService service, Mock<IEmailService> emailMock) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        var emailMock = new Mock<IEmailService>();
        emailMock
            .Setup(e => e.EnqueueAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var service = new BillingService(
            db,
            AuditMock.Object,
            NotificationMock.Object,
            emailMock.Object,
            JtiMock.Object,
            LoggerMock.Object);

        return (db, service, emailMock);
    }

    /// <summary>
    /// Semilla un Comercio con Plan Básico, una Suscripción trial, y usuarios Dueño y Gerente.
    /// </summary>
    private static (Comercio comercio, Usuario dueno, Usuario gerente, Suscripcion suscripcion) SeedTrialConUsuarios(
        AppDbContext db,
        int diasRestantes,
        string duenoEmail,
        string gerenteEmail)
    {
        var plan = new Plan
        {
            Id = 1,
            Nombre = "Básico",
            Precio = 350m,
            LimiteUsuarios = 2,
            LimiteAtributos = 2,
            LimiteSucursales = 1
        };
        db.Planes.Add(plan);

        var comercio = new Comercio
        {
            Ruc = "1234567890123",
            RazonSocial = "Comercio de Prueba",
            PlanId = 1,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow.AddDays(-10)
        };
        db.Comercios.Add(comercio);
        db.SaveChanges();

        // Crear usuario Dueño
        var dueno = new Usuario
        {
            ComercioId = comercio.Id,
            Nombre = "Juan Dueño",
            Email = duenoEmail,
            Rol = "Dueño",
            Activo = true,
            PasswordHash = "hash_dueno"
        };
        db.Usuarios.Add(dueno);

        // Crear usuario Gerente
        var gerente = new Usuario
        {
            ComercioId = comercio.Id,
            Nombre = "María Gerente",
            Email = gerenteEmail,
            Rol = "Gerente",
            Activo = true,
            PasswordHash = "hash_gerente"
        };
        db.Usuarios.Add(gerente);

        // Crear suscripción trial con FechaProximoCorte que dispara notificación
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = 1,
            FechaInicio = hoy.AddDays(-10),
            FechaProximoCorte = hoy.AddDays(diasRestantes),
            MontoCuota = 0m,
            EsProporcional = false,
            Estado = "Trial"
        };
        db.Suscripciones.Add(suscripcion);
        db.SaveChanges();

        return (comercio, dueno, gerente, suscripcion);
    }

    // ─── Property 17: Notificaciones de trial al Dueño ──────────────────────

    /// <summary>
    /// Para cualquier trial que dispara notificación (5, 2, o 0 días restantes),
    /// el email se envía SIEMPRE al usuario con rol Dueño.
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NotificacionTrial_DestinatarioEsSiempreElDueno()
    {
        // Generar días restantes aleatorios que disparan notificación
        var diasGen = Gen.Elements(5, 2, 0);

        return Prop.ForAll(
            diasGen.ToArbitrary(),
            (diasRestantes) =>
            {
                var (db, service, emailMock) = CreateContext();
                using (db)
                {
                    var duenoEmail = $"dueno_{Guid.NewGuid():N}@test.com";
                    var gerenteEmail = $"gerente_{Guid.NewGuid():N}@test.com";

                    SeedTrialConUsuarios(db, diasRestantes, duenoEmail, gerenteEmail);

                    // Ejecutar procesamiento diario
                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    // Verificar que EnqueueAsync fue llamado con el email del Dueño
                    emailMock.Verify(
                        e => e.EnqueueAsync(
                            It.IsAny<int?>(),
                            duenoEmail,
                            It.IsAny<string>(),
                            It.IsAny<string>()),
                        Times.Once());

                    return true.ToProperty();
                }
            });
    }

    /// <summary>
    /// Para cualquier trial que dispara notificación, el email del Gerente
    /// NUNCA se utiliza como destinatario.
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NotificacionTrial_GerenteNuncaEsDestinatario()
    {
        var diasGen = Gen.Elements(5, 2, 0);

        return Prop.ForAll(
            diasGen.ToArbitrary(),
            (diasRestantes) =>
            {
                var (db, service, emailMock) = CreateContext();
                using (db)
                {
                    var duenoEmail = $"dueno_{Guid.NewGuid():N}@test.com";
                    var gerenteEmail = $"gerente_{Guid.NewGuid():N}@test.com";

                    SeedTrialConUsuarios(db, diasRestantes, duenoEmail, gerenteEmail);

                    // Ejecutar procesamiento diario
                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    // Verificar que NUNCA se llamó con el email del Gerente
                    emailMock.Verify(
                        e => e.EnqueueAsync(
                            It.IsAny<int?>(),
                            gerenteEmail,
                            It.IsAny<string>(),
                            It.IsAny<string>()),
                        Times.Never());

                    return true.ToProperty();
                }
            });
    }

    /// <summary>
    /// Cuando no existe un usuario Dueño activo (solo Gerente), no se envía email
    /// pero la notificación SÍ se crea en la base de datos.
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NotificacionTrial_SinDueno_NoEnviaEmailPeroCreanNotificacion()
    {
        var diasGen = Gen.Elements(5, 2, 0);

        return Prop.ForAll(
            diasGen.ToArbitrary(),
            (diasRestantes) =>
            {
                var (db, service, emailMock) = CreateContext();
                using (db)
                {
                    // Sembrar datos SIN usuario Dueño, solo Gerente
                    var plan = new Plan
                    {
                        Id = 1,
                        Nombre = "Básico",
                        Precio = 350m,
                        LimiteUsuarios = 2,
                        LimiteAtributos = 2,
                        LimiteSucursales = 1
                    };
                    db.Planes.Add(plan);

                    var comercio = new Comercio
                    {
                        Ruc = "9876543210123",
                        RazonSocial = "Comercio Sin Dueño",
                        PlanId = 1,
                        Estado = "Activo",
                        FechaRegistro = DateTime.UtcNow.AddDays(-10)
                    };
                    db.Comercios.Add(comercio);
                    db.SaveChanges();

                    // Solo crear Gerente, sin Dueño
                    var gerente = new Usuario
                    {
                        ComercioId = comercio.Id,
                        Nombre = "Solo Gerente",
                        Email = "gerente_solo@test.com",
                        Rol = "Gerente",
                        Activo = true,
                        PasswordHash = "hash_gerente"
                    };
                    db.Usuarios.Add(gerente);

                    var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
                    var suscripcion = new Suscripcion
                    {
                        ComercioId = comercio.Id,
                        PlanId = 1,
                        FechaInicio = hoy.AddDays(-10),
                        FechaProximoCorte = hoy.AddDays(diasRestantes),
                        MontoCuota = 0m,
                        EsProporcional = false,
                        Estado = "Trial"
                    };
                    db.Suscripciones.Add(suscripcion);
                    db.SaveChanges();

                    // Ejecutar procesamiento diario
                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    // Verificar que NO se envió ningún email
                    emailMock.Verify(
                        e => e.EnqueueAsync(
                            It.IsAny<int?>(),
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            It.IsAny<string>()),
                        Times.Never());

                    // Verificar que SÍ se creó la notificación en la base de datos
                    var notificacionCreada = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Any(n => n.SuscripcionId == suscripcion.Id);

                    return notificacionCreada
                        .Label($"diasRestantes={diasRestantes}, notificacionCreada={notificacionCreada}");
                }
            });
    }
}
