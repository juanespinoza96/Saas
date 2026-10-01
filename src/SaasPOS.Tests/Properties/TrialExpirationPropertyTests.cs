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

// Feature: prueba-gratuita, Property 8: Expiración automática de trial

/// <summary>
/// Property-based tests para la expiración automática de trial.
/// **Validates: Requirements 4.1**
///
/// Property 8: Expiración automática de trial.
/// "Para cualquier Suscripción con Estado 'Trial' cuya FechaProximoCorte es menor o igual
/// a la fecha actual del ciclo, el BillingService SHALL cambiar Suscripcion.Estado a
/// 'Trial_Expirado', Comercio.Estado a 'Suspendido', e invalidar todas las sesiones
/// activas del comercio."
/// </summary>
public class TrialExpirationPropertyTests
{
    private static readonly Mock<IAuditService> AuditMock = new();
    private static readonly Mock<INotificationService> NotificationMock = new();
    private static readonly Mock<IEmailService> EmailMock = new();
    private static readonly Mock<ILogger<BillingService>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto de base de datos InMemory con advertencias de transacciones suprimidas
    /// y un BillingService con mocks de dependencias.
    /// Retorna también el Mock de IJtiBlocklist para poder verificar llamadas.
    /// </summary>
    private static (AppDbContext db, BillingService service, Mock<IJtiBlocklist> jtiMock) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        var jtiMock = new Mock<IJtiBlocklist>();

        var service = new BillingService(
            db,
            AuditMock.Object,
            NotificationMock.Object,
            EmailMock.Object,
            jtiMock.Object,
            LoggerMock.Object);

        return (db, service, jtiMock);
    }

    /// <summary>
    /// Semilla los datos base para un trial: Plan, Comercio, Usuario Dueño y Suscripción Trial.
    /// El parámetro diasOffset indica cuántos días antes (negativo) o después (positivo) de hoy
    /// se establece la FechaProximoCorte.
    /// </summary>
    private static (Plan plan, Comercio comercio, Suscripcion suscripcion) SeedTrial(
        AppDbContext db, int diasOffset)
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
            Ruc = "0912345678001",
            RazonSocial = "Comercio Test",
            PlanId = plan.Id,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow.AddDays(-15)
        };
        db.Comercios.Add(comercio);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var fechaProximoCorte = hoy.AddDays(diasOffset);

        var suscripcion = new Suscripcion
        {
            Id = 1,
            ComercioId = comercio.Id,
            PlanId = plan.Id,
            FechaInicio = fechaProximoCorte.AddDays(-15),
            FechaProximoCorte = fechaProximoCorte,
            MontoCuota = 0m,
            EsProporcional = false,
            Estado = "Trial"
        };
        db.Suscripciones.Add(suscripcion);

        // Usuario Dueño requerido para relaciones válidas
        var usuario = new Usuario
        {
            Id = 1,
            ComercioId = comercio.Id,
            Nombre = "Dueño Test",
            Email = "dueno@test.com",
            PasswordHash = "hash",
            Rol = "Dueño",
            Activo = true
        };
        db.Usuarios.Add(usuario);

        db.SaveChanges();

        return (plan, comercio, suscripcion);
    }

    // ─── Property 8a: Trial con FechaProximoCorte == hoy (diasRestantes = 0) expira ──────

    /// <summary>
    /// Para cualquier trial con FechaProximoCorte == hoy (diasRestantes = 0),
    /// ProcesarCortesDiariosAsync cambia Suscripcion.Estado a "Trial_Expirado".
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialConFechaCorteHoy_SuscripcionCambiaATrialExpirado()
    {
        // Generar 0 días de offset (siempre FechaProximoCorte == hoy)
        return Prop.ForAll(
            Gen.Constant(0).ToArbitrary(),
            (int _) =>
            {
                var (db, service, jtiMock) = CreateContext();
                using (db)
                {
                    SeedTrial(db, diasOffset: 0);

                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == 1);

                    return (suscripcion.Estado == "Trial_Expirado")
                        .Label($"Estado esperado: Trial_Expirado, Actual: {suscripcion.Estado}");
                }
            });
    }

    /// <summary>
    /// Para cualquier trial con FechaProximoCorte == hoy (diasRestantes = 0),
    /// ProcesarCortesDiariosAsync cambia Comercio.Estado a "Suspendido".
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialConFechaCorteHoy_ComercioCambiaASuspendido()
    {
        return Prop.ForAll(
            Gen.Constant(0).ToArbitrary(),
            (int _) =>
            {
                var (db, service, jtiMock) = CreateContext();
                using (db)
                {
                    SeedTrial(db, diasOffset: 0);

                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    var comercio = db.Comercios
                        .IgnoreQueryFilters()
                        .First(c => c.Id == 1);

                    return (comercio.Estado == "Suspendido")
                        .Label($"Estado esperado: Suspendido, Actual: {comercio.Estado}");
                }
            });
    }

    /// <summary>
    /// Para cualquier trial con FechaProximoCorte == hoy (diasRestantes = 0),
    /// ProcesarCortesDiariosAsync invoca BlockAllForComercio con el comercioId correcto.
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialConFechaCorteHoy_InvalidaSesiones()
    {
        return Prop.ForAll(
            Gen.Constant(0).ToArbitrary(),
            (int _) =>
            {
                var (db, service, jtiMock) = CreateContext();
                using (db)
                {
                    SeedTrial(db, diasOffset: 0);

                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    try
                    {
                        jtiMock.Verify(j => j.BlockAllForComercio(1), Times.AtLeastOnce());
                        return true.ToProperty();
                    }
                    catch
                    {
                        return false.Label("BlockAllForComercio no fue invocado para comercioId=1");
                    }
                }
            });
    }

    // ─── Property 8b: Trial con FechaProximoCorte < hoy (diasRestantes < 0) expira ──────

    /// <summary>
    /// Para cualquier trial con FechaProximoCorte en el pasado (1 a 30 días atrás),
    /// ProcesarCortesDiariosAsync cambia Suscripcion.Estado a "Trial_Expirado".
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialConFechaCorteEnPasado_SuscripcionCambiaATrialExpirado()
    {
        // Generar días pasados: -1 a -30 (FechaProximoCorte antes de hoy)
        return Prop.ForAll(
            Gen.Choose(1, 30).Select(d => -d).ToArbitrary(),
            (int diasPasados) =>
            {
                var (db, service, jtiMock) = CreateContext();
                using (db)
                {
                    SeedTrial(db, diasOffset: diasPasados);

                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == 1);

                    return (suscripcion.Estado == "Trial_Expirado")
                        .Label($"diasPasados={diasPasados}, Estado esperado: Trial_Expirado, Actual: {suscripcion.Estado}");
                }
            });
    }

    /// <summary>
    /// Para cualquier trial con FechaProximoCorte en el pasado (1 a 30 días atrás),
    /// ProcesarCortesDiariosAsync cambia Comercio.Estado a "Suspendido".
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialConFechaCorteEnPasado_ComercioCambiaASuspendido()
    {
        return Prop.ForAll(
            Gen.Choose(1, 30).Select(d => -d).ToArbitrary(),
            (int diasPasados) =>
            {
                var (db, service, jtiMock) = CreateContext();
                using (db)
                {
                    SeedTrial(db, diasOffset: diasPasados);

                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    var comercio = db.Comercios
                        .IgnoreQueryFilters()
                        .First(c => c.Id == 1);

                    return (comercio.Estado == "Suspendido")
                        .Label($"diasPasados={diasPasados}, Estado esperado: Suspendido, Actual: {comercio.Estado}");
                }
            });
    }

    /// <summary>
    /// Para cualquier trial con FechaProximoCorte en el pasado (1 a 30 días atrás),
    /// ProcesarCortesDiariosAsync invoca BlockAllForComercio con el comercioId correcto.
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialConFechaCorteEnPasado_InvalidaSesiones()
    {
        return Prop.ForAll(
            Gen.Choose(1, 30).Select(d => -d).ToArbitrary(),
            (int diasPasados) =>
            {
                var (db, service, jtiMock) = CreateContext();
                using (db)
                {
                    SeedTrial(db, diasOffset: diasPasados);

                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    try
                    {
                        jtiMock.Verify(j => j.BlockAllForComercio(1), Times.AtLeastOnce());
                        return true.ToProperty();
                    }
                    catch
                    {
                        return false.Label($"diasPasados={diasPasados}, BlockAllForComercio no fue invocado para comercioId=1");
                    }
                }
            });
    }

    // ─── Property 8c: Trial con FechaProximoCorte > hoy (diasRestantes > 0) NO expira ──────

    /// <summary>
    /// Para cualquier trial con FechaProximoCorte en el futuro (1 a 30 días adelante),
    /// ProcesarCortesDiariosAsync NO cambia Suscripcion.Estado (permanece "Trial").
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialConFechaCorteEnFuturo_SuscripcionPermaneceTrial()
    {
        // Generar días futuros: 1 a 30 (FechaProximoCorte después de hoy)
        return Prop.ForAll(
            Gen.Choose(1, 30).ToArbitrary(),
            (int diasFuturos) =>
            {
                var (db, service, jtiMock) = CreateContext();
                using (db)
                {
                    SeedTrial(db, diasOffset: diasFuturos);

                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == 1);

                    return (suscripcion.Estado == "Trial")
                        .Label($"diasFuturos={diasFuturos}, Estado esperado: Trial, Actual: {suscripcion.Estado}");
                }
            });
    }

    /// <summary>
    /// Para cualquier trial con FechaProximoCorte en el futuro (1 a 30 días adelante),
    /// ProcesarCortesDiariosAsync NO cambia Comercio.Estado (permanece "Activo").
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialConFechaCorteEnFuturo_ComercioPermanecActivo()
    {
        return Prop.ForAll(
            Gen.Choose(1, 30).ToArbitrary(),
            (int diasFuturos) =>
            {
                var (db, service, jtiMock) = CreateContext();
                using (db)
                {
                    SeedTrial(db, diasOffset: diasFuturos);

                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    var comercio = db.Comercios
                        .IgnoreQueryFilters()
                        .First(c => c.Id == 1);

                    return (comercio.Estado == "Activo")
                        .Label($"diasFuturos={diasFuturos}, Estado esperado: Activo, Actual: {comercio.Estado}");
                }
            });
    }

    /// <summary>
    /// Para cualquier trial con FechaProximoCorte en el futuro (1 a 30 días adelante),
    /// ProcesarCortesDiariosAsync NO invoca BlockAllForComercio.
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrialConFechaCorteEnFuturo_NoInvalidaSesiones()
    {
        return Prop.ForAll(
            Gen.Choose(1, 30).ToArbitrary(),
            (int diasFuturos) =>
            {
                var (db, service, jtiMock) = CreateContext();
                using (db)
                {
                    SeedTrial(db, diasOffset: diasFuturos);

                    service.ProcesarCortesDiariosAsync(DateTime.UtcNow).GetAwaiter().GetResult();

                    try
                    {
                        jtiMock.Verify(j => j.BlockAllForComercio(It.IsAny<int>()), Times.Never());
                        return true.ToProperty();
                    }
                    catch
                    {
                        return false.Label($"diasFuturos={diasFuturos}, BlockAllForComercio fue invocado pero NO debería");
                    }
                }
            });
    }
}
