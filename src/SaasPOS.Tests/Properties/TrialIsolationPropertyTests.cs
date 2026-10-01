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

// Feature: prueba-gratuita, Property 10: Aislamiento del trial en el ciclo de facturación

/// <summary>
/// Property-based tests para el aislamiento del trial en el ciclo de facturación.
/// **Validates: Requirements 9.2**
///
/// Property 10: Aislamiento del trial en el ciclo de facturación.
/// "Para cualquier Suscripción con Estado 'Trial', el procesamiento diario de cortes
/// SHALL NO ejecutar la lógica de cobros, la transición a 'Por vencer',
/// la transición a 'En mora', ni la suspensión por mora de 3 días."
/// </summary>
public class TrialIsolationPropertyTests
{
    private static readonly Mock<IAuditService> AuditMock = new();
    private static readonly Mock<IEmailService> EmailMock = new();
    private static readonly Mock<IJtiBlocklist> JtiMock = new();
    private static readonly Mock<ILogger<BillingService>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto InMemory y un BillingService con dependencias mockeadas.
    /// Retorna también el mock de INotificationService para verificar que NO se llaman
    /// los métodos de cobros regulares.
    /// </summary>
    private static (AppDbContext db, BillingService service, Mock<INotificationService> notificationMock) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);
        var notificationMock = new Mock<INotificationService>();
        var service = new BillingService(
            db,
            AuditMock.Object,
            notificationMock.Object,
            EmailMock.Object,
            JtiMock.Object,
            LoggerMock.Object);

        return (db, service, notificationMock);
    }

    /// <summary>
    /// Semilla un Plan, Comercio, Usuario (Dueño) y Suscripcion con estado y fecha dados.
    /// </summary>
    private static Suscripcion SeedTrialSubscription(
        AppDbContext db,
        string estado,
        DateOnly fechaProximoCorte)
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
            Id = 1,
            Ruc = "1234567890001",
            RazonSocial = "Comercio Test",
            PlanId = 1,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow
        };
        db.Comercios.Add(comercio);

        var usuario = new Usuario
        {
            Id = 1,
            ComercioId = 1,
            Nombre = "Dueño Test",
            Email = "dueno@test.com",
            PasswordHash = "hash",
            Rol = "Dueño",
            Activo = true
        };
        db.Usuarios.Add(usuario);

        var suscripcion = new Suscripcion
        {
            Id = 1,
            ComercioId = 1,
            PlanId = 1,
            FechaInicio = fechaProximoCorte.AddDays(-15),
            FechaProximoCorte = fechaProximoCorte,
            MontoCuota = 0m,
            EsProporcional = false,
            Estado = estado,
            FechaUltimoPago = null
        };
        db.Suscripciones.Add(suscripcion);

        db.SaveChanges();
        return suscripcion;
    }

    // ─── Property: Trial con FechaProximoCorte a 7 días NO transiciona a "Por vencer" ───

    /// <summary>
    /// Una suscripción Trial con FechaProximoCorte exactamente 7 días adelante
    /// NO debe transicionar a "Por vencer" (esa lógica solo aplica a "Activa").
    /// El estado debe permanecer "Trial".
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_ConCorte7DiasAdelante_NoPasaAPorVencer()
    {
        // Generador de offsets de días para ubicar "hoy" en distintas fechas
        var genDayOffset = Gen.Choose(0, 365);

        return Prop.ForAll(
            genDayOffset.ToArbitrary(),
            (dayOffset) =>
            {
                var (db, service, notificationMock) = CreateContext();
                using (db)
                {
                    // "hoy" es un día arbitrario; FechaProximoCorte = hoy + 7
                    var hoy = new DateOnly(2025, 1, 1).AddDays(dayOffset);
                    var fechaCorte = hoy.AddDays(7);

                    SeedTrialSubscription(db, "Trial", fechaCorte);

                    // Ejecutar el procesamiento diario
                    service.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue))
                        .GetAwaiter().GetResult();

                    // Verificar que el estado NO cambió a "Por vencer"
                    var suscripcion = db.Suscripciones.IgnoreQueryFilters().First();

                    // El trial no debe pasar a "Por vencer"
                    var noPorVencer = suscripcion.Estado != "Por vencer";

                    // Además, CrearNotificacionPagoProximoAsync no debe haberse llamado
                    notificationMock.Verify(
                        n => n.CrearNotificacionPagoProximoAsync(
                            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<DateTime>()),
                        Times.Never());

                    return noPorVencer
                        .Label($"Estado={suscripcion.Estado}, esperado != 'Por vencer'");
                }
            });
    }

    // ─── Property: Trial con FechaProximoCorte == hoy NO transiciona a "En mora" ───

    /// <summary>
    /// Una suscripción Trial con FechaProximoCorte igual a hoy NO debe transicionar
    /// a "En mora" (esa lógica solo aplica a "Activa"/"Por vencer").
    /// En cambio, el procesamiento de trials la lleva a "Trial_Expirado".
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_ConCorteHoy_NoPasaAEnMora()
    {
        var genDayOffset = Gen.Choose(0, 365);

        return Prop.ForAll(
            genDayOffset.ToArbitrary(),
            (dayOffset) =>
            {
                var (db, service, notificationMock) = CreateContext();
                using (db)
                {
                    var hoy = new DateOnly(2025, 1, 1).AddDays(dayOffset);

                    // FechaProximoCorte = hoy → triggería "En mora" si fuera "Activa"
                    SeedTrialSubscription(db, "Trial", hoy);

                    service.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue))
                        .GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones.IgnoreQueryFilters().First();

                    // NO debe ser "En mora" — debe ser "Trial_Expirado" (vía lógica de trial)
                    var noEnMora = suscripcion.Estado != "En mora";
                    var esTrialExpirado = suscripcion.Estado == "Trial_Expirado";

                    // CrearNotificacionMoraAsync NO debe haberse llamado
                    notificationMock.Verify(
                        n => n.CrearNotificacionMoraAsync(It.IsAny<int>()),
                        Times.Never());

                    return (noEnMora && esTrialExpirado)
                        .Label($"Estado={suscripcion.Estado}, esperado='Trial_Expirado', no 'En mora'");
                }
            });
    }

    // ─── Property: Trial NUNCA transiciona a "Suspendido" por mora ───

    /// <summary>
    /// Una suscripción Trial con FechaProximoCorte 3 días en el pasado NO debe
    /// transicionar a "Suspendido" por lógica de mora (esa lógica solo aplica a "En mora").
    /// La suspensión por trial expirado es por la ruta Trial → Trial_Expirado (no "Suspendido" en suscripcion).
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_ConCorte3DiasAtras_NoSuspendidoPorMora()
    {
        var genDayOffset = Gen.Choose(0, 365);

        return Prop.ForAll(
            genDayOffset.ToArbitrary(),
            (dayOffset) =>
            {
                var (db, service, notificationMock) = CreateContext();
                using (db)
                {
                    var hoy = new DateOnly(2025, 1, 1).AddDays(dayOffset);
                    var fechaCorte = hoy.AddDays(-3);

                    // FechaProximoCorte = hoy - 3 → triggería suspensión por mora si fuera "En mora"
                    SeedTrialSubscription(db, "Trial", fechaCorte);

                    service.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue))
                        .GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones.IgnoreQueryFilters().First();

                    // NO debe ser "Suspendido" como suscripcion.Estado
                    // El trial expirado pone "Trial_Expirado" en suscripcion, "Suspendido" en Comercio
                    var noSuspendidoEnSuscripcion = suscripcion.Estado != "Suspendido";
                    var esTrialExpirado = suscripcion.Estado == "Trial_Expirado";

                    // Las notificaciones de cobro regular NO deben haberse llamado
                    notificationMock.Verify(
                        n => n.CrearNotificacionPagoProximoAsync(
                            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<DateTime>()),
                        Times.Never());
                    notificationMock.Verify(
                        n => n.CrearNotificacionMoraAsync(It.IsAny<int>()),
                        Times.Never());

                    return (noSuspendidoEnSuscripcion && esTrialExpirado)
                        .Label($"Estado={suscripcion.Estado}, esperado='Trial_Expirado', no 'Suspendido'");
                }
            });
    }

    // ─── Property: Trial_Expirado NO es procesado por ciclo de cobros regular ───

    /// <summary>
    /// Una suscripción con Estado "Trial_Expirado" NO debe ser procesada por la lógica
    /// de cobros regulares (no transiciona a "Por vencer", "En mora", ni "Suspendido").
    /// El estado debe permanecer "Trial_Expirado" sin importar FechaProximoCorte.
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialExpirado_NoProcesadoPorCobrosRegulares()
    {
        // Generador de días de offset para la fecha de corte relativa a hoy
        // Incluye -3, 0, 7 y otros valores para cubrir todos los triggers de cobro
        var genCorteOffset = Gen.Elements(-3, -2, -1, 0, 1, 2, 5, 7, 10);

        return Prop.ForAll(
            genCorteOffset.ToArbitrary(),
            (corteOffset) =>
            {
                var (db, service, notificationMock) = CreateContext();
                using (db)
                {
                    var hoy = new DateOnly(2025, 6, 15);
                    var fechaCorte = hoy.AddDays(corteOffset);

                    // Semilla con estado Trial_Expirado
                    SeedTrialSubscription(db, "Trial_Expirado", fechaCorte);

                    // Comercio ya está suspendido (como sería después de expiración)
                    var comercio = db.Comercios.IgnoreQueryFilters().First();
                    comercio.Estado = "Suspendido";
                    db.SaveChanges();

                    service.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue))
                        .GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones.IgnoreQueryFilters().First();

                    // El estado debe permanecer Trial_Expirado
                    var sigueTrialExpirado = suscripcion.Estado == "Trial_Expirado";

                    // Ninguna notificación de cobro regular debe haberse disparado
                    notificationMock.Verify(
                        n => n.CrearNotificacionPagoProximoAsync(
                            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<DateTime>()),
                        Times.Never());
                    notificationMock.Verify(
                        n => n.CrearNotificacionMoraAsync(It.IsAny<int>()),
                        Times.Never());

                    return sigueTrialExpirado
                        .Label($"Estado={suscripcion.Estado}, corteOffset={corteOffset}, esperado='Trial_Expirado'");
                }
            });
    }
}
