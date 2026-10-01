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

// Feature: prueba-gratuita, Property 6: Notificaciones de trial según días restantes

/// <summary>
/// Property-based tests para las notificaciones de trial según días restantes.
/// Verifica que para cualquier Suscripción Trial, las notificaciones correctas se generan
/// según los días restantes (5, 2, 0) con contenido adecuado.
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
/// </summary>
public class TrialNotificationsPropertyTests
{
    /// <summary>
    /// Crea un contexto InMemory con BillingService configurado con mocks.
    /// </summary>
    private static (AppDbContext db, BillingService service) CreateBillingContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        var service = new BillingService(
            db,
            Mock.Of<IAuditService>(),
            Mock.Of<INotificationService>(),
            Mock.Of<IEmailService>(),
            Mock.Of<IJtiBlocklist>(),
            Mock.Of<ILogger<BillingService>>());

        return (db, service);
    }

    /// <summary>
    /// Siembra un Comercio con Suscripción Trial y un Usuario Dueño activo.
    /// Retorna el ID de la suscripción creada.
    /// </summary>
    private static int SeedTrialConDueno(AppDbContext db, string razonSocial, DateOnly fechaProximoCorte)
    {
        // Sembrar Plan Básico si no existe
        if (!db.Planes.Any())
        {
            db.Planes.Add(new Plan
            {
                Id = 1,
                Nombre = "Básico",
                Precio = 350m,
                LimiteUsuarios = 2,
                LimiteAtributos = 2,
                LimiteSucursales = 1
            });
            db.SaveChanges();
        }

        var comercio = new Comercio
        {
            Ruc = Guid.NewGuid().ToString("N")[..13],
            RazonSocial = razonSocial,
            PlanId = 1,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow.AddDays(-30)
        };
        db.Comercios.Add(comercio);
        db.SaveChanges();

        // Crear usuario Dueño activo para que el email se encole
        var usuario = new Usuario
        {
            ComercioId = comercio.Id,
            Nombre = "Dueño Test",
            Email = "dueno@test.com",
            PasswordHash = "hash",
            Rol = "Dueño",
            Activo = true
        };
        db.Usuarios.Add(usuario);
        db.SaveChanges();

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = 1,
            FechaInicio = fechaProximoCorte.AddDays(-15),
            FechaProximoCorte = fechaProximoCorte,
            MontoCuota = 0,
            EsProporcional = false,
            Estado = "Trial",
            DiasExtendidos = 0
        };
        db.Suscripciones.Add(suscripcion);
        db.SaveChanges();

        return suscripcion.Id;
    }

    // ─── Property 6: Notificación "Trial por Expirar" a 5 días ───────────────

    /// <summary>
    /// Para cualquier fecha base, si FechaProximoCorte está exactamente 5 días después de hoy,
    /// el BillingService crea una notificación con TipoNotificacion = "Trial por Expirar".
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Notificacion_TrialPorExpirar_CuandoFaltan5Dias()
    {
        // Generamos un offset de base entre 0 y 365 para variar la fecha "hoy"
        return Prop.ForAll(
            Gen.Choose(0, 365).ToArbitrary(),
            (int baseDayOffset) =>
            {
                var (db, service) = CreateBillingContext();
                using (db)
                {
                    // "hoy" es una fecha variable basada en el offset generado
                    var hoy = new DateOnly(2024, 1, 1).AddDays(baseDayOffset);
                    var fechaProximoCorte = hoy.AddDays(5);
                    var razonSocial = $"Comercio5D_{baseDayOffset}";

                    SeedTrialConDueno(db, razonSocial, fechaProximoCorte);

                    // Ejecutar procesamiento de cortes con la fecha "hoy"
                    service.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue))
                        .GetAwaiter().GetResult();

                    // Verificar que se creó la notificación correcta
                    var notificacion = db.Notificaciones
                        .IgnoreQueryFilters()
                        .FirstOrDefault(n => n.TipoNotificacion == "Trial por Expirar");

                    return (notificacion != null)
                        .Label($"BaseDayOffset={baseDayOffset}, Hoy={hoy}, FechaCorte={fechaProximoCorte}, " +
                               $"Notificacion encontrada: {notificacion != null}");
                }
            });
    }

    // ─── Property 6: Notificación "Trial Urgente" a 2 días ───────────────────

    /// <summary>
    /// Para cualquier fecha base, si FechaProximoCorte está exactamente 2 días después de hoy,
    /// el BillingService crea una notificación con TipoNotificacion = "Trial Urgente".
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Notificacion_TrialUrgente_CuandoFaltan2Dias()
    {
        return Prop.ForAll(
            Gen.Choose(0, 365).ToArbitrary(),
            (int baseDayOffset) =>
            {
                var (db, service) = CreateBillingContext();
                using (db)
                {
                    var hoy = new DateOnly(2024, 1, 1).AddDays(baseDayOffset);
                    var fechaProximoCorte = hoy.AddDays(2);
                    var razonSocial = $"Comercio2D_{baseDayOffset}";

                    SeedTrialConDueno(db, razonSocial, fechaProximoCorte);

                    service.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue))
                        .GetAwaiter().GetResult();

                    var notificacion = db.Notificaciones
                        .IgnoreQueryFilters()
                        .FirstOrDefault(n => n.TipoNotificacion == "Trial Urgente");

                    return (notificacion != null)
                        .Label($"BaseDayOffset={baseDayOffset}, Hoy={hoy}, FechaCorte={fechaProximoCorte}, " +
                               $"Notificacion encontrada: {notificacion != null}");
                }
            });
    }

    // ─── Property 6: Notificación "Trial Expirado" a 0 días ─────────────────

    /// <summary>
    /// Para cualquier fecha base, si FechaProximoCorte es igual a hoy (0 días restantes),
    /// el BillingService crea una notificación con TipoNotificacion = "Trial Expirado".
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Notificacion_TrialExpirado_CuandoFaltan0Dias()
    {
        return Prop.ForAll(
            Gen.Choose(0, 365).ToArbitrary(),
            (int baseDayOffset) =>
            {
                var (db, service) = CreateBillingContext();
                using (db)
                {
                    var hoy = new DateOnly(2024, 1, 1).AddDays(baseDayOffset);
                    var fechaProximoCorte = hoy; // 0 días restantes
                    var razonSocial = $"Comercio0D_{baseDayOffset}";

                    SeedTrialConDueno(db, razonSocial, fechaProximoCorte);

                    service.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue))
                        .GetAwaiter().GetResult();

                    var notificacion = db.Notificaciones
                        .IgnoreQueryFilters()
                        .FirstOrDefault(n => n.TipoNotificacion == "Trial Expirado");

                    return (notificacion != null)
                        .Label($"BaseDayOffset={baseDayOffset}, Hoy={hoy}, FechaCorte={fechaProximoCorte}, " +
                               $"Notificacion encontrada: {notificacion != null}");
                }
            });
    }

    // ─── Property 6: NO se crea notificación para otros días ─────────────────

    /// <summary>
    /// Para cualquier cantidad de días restantes distinta de 5, 2, o 0,
    /// el BillingService NO crea ninguna notificación de trial.
    /// **Validates: Requirements 3.1, 3.2, 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SinNotificacion_CuandoDiasNoSon5_2_0()
    {
        // Generar días restantes excluyendo 5, 2 y 0 (rango 1-30 sin esos valores)
        var diasGen = Gen.Choose(1, 30)
            .Where(d => d != 5 && d != 2 && d != 0)
            .ToArbitrary();

        return Prop.ForAll(
            diasGen,
            Gen.Choose(0, 365).ToArbitrary(),
            (int diasRestantes, int baseDayOffset) =>
            {
                var (db, service) = CreateBillingContext();
                using (db)
                {
                    var hoy = new DateOnly(2024, 1, 1).AddDays(baseDayOffset);
                    var fechaProximoCorte = hoy.AddDays(diasRestantes);
                    var razonSocial = $"ComercioND_{baseDayOffset}_{diasRestantes}";

                    SeedTrialConDueno(db, razonSocial, fechaProximoCorte);

                    service.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue))
                        .GetAwaiter().GetResult();

                    var notificaciones = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Where(n => n.TipoNotificacion == "Trial por Expirar"
                                 || n.TipoNotificacion == "Trial Urgente"
                                 || n.TipoNotificacion == "Trial Expirado")
                        .ToList();

                    return (notificaciones.Count == 0)
                        .Label($"DiasRestantes={diasRestantes}, BaseDayOffset={baseDayOffset}, " +
                               $"Notificaciones creadas: {notificaciones.Count}");
                }
            });
    }

    // ─── Property 6: Mensaje contiene RazonSocial ────────────────────────────

    /// <summary>
    /// Para TODAS las notificaciones de trial generadas (5, 2, 0 días),
    /// el Mensaje debe contener la RazonSocial del comercio.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Mensaje_ContieneRazonSocial_EnTodasLasNotificaciones()
    {
        // Generar uno de los días que producen notificación: 5, 2, o 0
        var diasNotificacionGen = Gen.Elements(5, 2, 0).ToArbitrary();

        return Prop.ForAll(
            diasNotificacionGen,
            Gen.Choose(0, 365).ToArbitrary(),
            (int diasRestantes, int baseDayOffset) =>
            {
                var (db, service) = CreateBillingContext();
                using (db)
                {
                    var hoy = new DateOnly(2024, 1, 1).AddDays(baseDayOffset);
                    var fechaProximoCorte = hoy.AddDays(diasRestantes);
                    var razonSocial = $"MiComercio_{baseDayOffset}_{diasRestantes}";

                    SeedTrialConDueno(db, razonSocial, fechaProximoCorte);

                    service.ProcesarCortesDiariosAsync(hoy.ToDateTime(TimeOnly.MinValue))
                        .GetAwaiter().GetResult();

                    var notificacion = db.Notificaciones
                        .IgnoreQueryFilters()
                        .FirstOrDefault(n => n.TipoNotificacion == "Trial por Expirar"
                                          || n.TipoNotificacion == "Trial Urgente"
                                          || n.TipoNotificacion == "Trial Expirado");

                    // Verificar que el mensaje contiene la RazonSocial
                    var contieneRazonSocial = notificacion != null
                        && notificacion.Mensaje.Contains(razonSocial);

                    return contieneRazonSocial
                        .Label($"DiasRestantes={diasRestantes}, RazonSocial={razonSocial}, " +
                               $"Notificacion={notificacion?.TipoNotificacion ?? "null"}, " +
                               $"Mensaje={notificacion?.Mensaje ?? "null"}");
                }
            });
    }
}
