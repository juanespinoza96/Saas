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

// Feature: prueba-gratuita, Property 7: Idempotencia de notificaciones de trial

/// <summary>
/// Property-based tests para la idempotencia de notificaciones de trial.
/// **Validates: Requirements 3.5**
///
/// Property 7: Idempotencia de notificaciones de trial.
/// "Para cualquier Suscripción que ya tiene una Notificación con un TipoNotificacion específico
/// ('Trial por Expirar', 'Trial Urgente', o 'Trial Expirado'), ejecutar el procesamiento diario
/// nuevamente SHALL NO crear otra notificación del mismo tipo para la misma Suscripción."
/// </summary>
public class TrialIdempotencyPropertyTests
{
    /// <summary>
    /// Generador de escenarios para probar la idempotencia de notificaciones.
    /// Genera valores de diasRestantes que disparan notificaciones: 5, 2, 0.
    /// </summary>
    public static class IdempotencyArbitraries
    {
        /// <summary>
        /// Genera diasRestantes que disparan notificación (5 o 2).
        /// Se excluye 0 porque ese caso cambia el estado a Trial_Expirado.
        /// </summary>
        public static Arbitrary<int> DiasRestantesConEstadoTrial()
        {
            var gen = Gen.Elements(5, 2);
            return gen.ToArbitrary();
        }

        /// <summary>
        /// Genera diasRestantes incluyendo el caso de expiración (0).
        /// </summary>
        public static Arbitrary<int> DiasRestantesTodos()
        {
            var gen = Gen.Elements(5, 2, 0);
            return gen.ToArbitrary();
        }

        /// <summary>
        /// Genera un Id de comercio positivo aleatorio para variar escenarios.
        /// </summary>
        public static Arbitrary<int> ComercioIdPositivo()
        {
            var gen = Gen.Choose(1, 10000);
            return gen.ToArbitrary();
        }
    }

    /// <summary>
    /// Crea un contexto de base de datos InMemory con las advertencias de transacciones suprimidas
    /// y un BillingService listo para usar.
    /// </summary>
    private static (AppDbContext db, BillingService service) CreateContext()
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
    /// Semilla un escenario de trial completo: Plan, Comercio, Usuario Dueño, y Suscripcion Trial
    /// con FechaProximoCorte configurada según los días restantes indicados.
    /// </summary>
    private static (Comercio comercio, Suscripcion suscripcion) SeedTrialScenario(
        AppDbContext db, int comercioId, int diasRestantes, DateOnly hoy)
    {
        // Plan Básico
        if (!db.Planes.Any(p => p.Id == 1))
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

        // Comercio
        var comercio = new Comercio
        {
            Id = comercioId,
            Ruc = comercioId.ToString().PadLeft(13, '0'),
            RazonSocial = $"Comercio Test {comercioId}",
            PlanId = 1,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow
        };
        db.Comercios.Add(comercio);

        // Usuario Dueño (requerido para el envío de email)
        var usuario = new Usuario
        {
            ComercioId = comercioId,
            Nombre = "Dueño Test",
            Email = $"dueno{comercioId}@test.com",
            PasswordHash = "hash_placeholder",
            Rol = "Dueño",
            Activo = true
        };
        db.Usuarios.Add(usuario);

        // Suscripcion Trial con FechaProximoCorte que genera la notificación esperada
        var fechaCorte = hoy.AddDays(diasRestantes);
        var suscripcion = new Suscripcion
        {
            ComercioId = comercioId,
            PlanId = 1,
            FechaInicio = hoy.AddDays(-10), // Inicio hace 10 días
            FechaProximoCorte = fechaCorte,
            MontoCuota = 0m,
            EsProporcional = false,
            Estado = "Trial"
        };
        db.Suscripciones.Add(suscripcion);
        db.SaveChanges();

        return (comercio, suscripcion);
    }

    // ─── Property 7: Idempotencia de notificaciones (dias 5 y 2, estado permanece Trial) ───

    /// <summary>
    /// Para diasRestantes = 5 o 2, ejecutar ProcesarCortesDiariosAsync múltiples veces
    /// NO crea notificaciones duplicadas. El conteo de notificaciones permanece en 1
    /// después de la primera ejecución.
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ProcesarMultiplesVeces_NoCreaNotificacionesDuplicadas_EstadoTrial()
    {
        return Prop.ForAll(
            IdempotencyArbitraries.DiasRestantesConEstadoTrial(),
            IdempotencyArbitraries.ComercioIdPositivo(),
            (diasRestantes, comercioId) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    var hoy = new DateOnly(2025, 6, 15);
                    var (comercio, suscripcion) = SeedTrialScenario(db, comercioId, diasRestantes, hoy);
                    var fechaProceso = hoy.ToDateTime(TimeOnly.MinValue);

                    // Primera ejecución: crea la notificación
                    service.ProcesarCortesDiariosAsync(fechaProceso).GetAwaiter().GetResult();

                    var countDespuesPrimera = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Count(n => n.SuscripcionId == suscripcion.Id);

                    // Segunda ejecución: NO debe crear duplicado
                    service.ProcesarCortesDiariosAsync(fechaProceso).GetAwaiter().GetResult();

                    var countDespuesSegunda = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Count(n => n.SuscripcionId == suscripcion.Id);

                    // Tercera ejecución: sigue sin crear duplicado
                    service.ProcesarCortesDiariosAsync(fechaProceso).GetAwaiter().GetResult();

                    var countDespuesTercera = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Count(n => n.SuscripcionId == suscripcion.Id);

                    // Determinar el tipo esperado según diasRestantes
                    var tipoEsperado = diasRestantes switch
                    {
                        5 => "Trial por Expirar",
                        2 => "Trial Urgente",
                        _ => ""
                    };

                    // Verificar que solo existe exactamente 1 notificación del tipo esperado
                    var countTipoEspecifico = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Count(n => n.SuscripcionId == suscripcion.Id && n.TipoNotificacion == tipoEsperado);

                    return (countDespuesPrimera == 1 &&
                            countDespuesSegunda == 1 &&
                            countDespuesTercera == 1 &&
                            countTipoEspecifico == 1)
                        .Label($"diasRestantes={diasRestantes}, tipo={tipoEsperado}, " +
                               $"count1={countDespuesPrimera}, count2={countDespuesSegunda}, " +
                               $"count3={countDespuesTercera}, countTipo={countTipoEspecifico}");
                }
            });
    }

    // ─── Property 7: Idempotencia con expiración (día 0, estado cambia a Trial_Expirado) ───

    /// <summary>
    /// Para diasRestantes = 0, la primera ejecución crea exactamente 1 notificación "Trial Expirado"
    /// y cambia el estado a "Trial_Expirado". Las ejecuciones posteriores no encuentran la suscripción
    /// en estado "Trial" (ya es "Trial_Expirado"), por lo que no se crean duplicados.
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ProcesarMultiplesVeces_DiaExpiracion_CreaExactamenteUnaNotificacion()
    {
        return Prop.ForAll(
            IdempotencyArbitraries.ComercioIdPositivo(),
            (comercioId) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    var hoy = new DateOnly(2025, 6, 15);
                    var (comercio, suscripcion) = SeedTrialScenario(db, comercioId, 0, hoy);
                    var fechaProceso = hoy.ToDateTime(TimeOnly.MinValue);

                    // Primera ejecución: crea notificación y cambia estado a Trial_Expirado
                    service.ProcesarCortesDiariosAsync(fechaProceso).GetAwaiter().GetResult();

                    var countDespuesPrimera = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Count(n => n.SuscripcionId == suscripcion.Id
                                    && n.TipoNotificacion == "Trial Expirado");

                    // Verificar que el estado cambió a Trial_Expirado
                    var suscripcionActualizada = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == suscripcion.Id);
                    var estadoDespuesPrimera = suscripcionActualizada.Estado;

                    // Segunda ejecución: la suscripción ya no es "Trial", no se procesa de nuevo
                    service.ProcesarCortesDiariosAsync(fechaProceso).GetAwaiter().GetResult();

                    var countDespuesSegunda = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Count(n => n.SuscripcionId == suscripcion.Id
                                    && n.TipoNotificacion == "Trial Expirado");

                    // Tercera ejecución: aún sin duplicados
                    service.ProcesarCortesDiariosAsync(fechaProceso).GetAwaiter().GetResult();

                    var countDespuesTercera = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Count(n => n.SuscripcionId == suscripcion.Id
                                    && n.TipoNotificacion == "Trial Expirado");

                    return (countDespuesPrimera == 1 &&
                            estadoDespuesPrimera == "Trial_Expirado" &&
                            countDespuesSegunda == 1 &&
                            countDespuesTercera == 1)
                        .Label($"count1={countDespuesPrimera}, estado={estadoDespuesPrimera}, " +
                               $"count2={countDespuesSegunda}, count3={countDespuesTercera}");
                }
            });
    }

    // ─── Property 7: Idempotencia verificada por tipo específico ───

    /// <summary>
    /// Para cualquier tipo de notificación de trial (5, 2, 0 días), si ya existe una notificación
    /// con ese TipoNotificacion para la misma Suscripcion, el procesamiento diario no crea duplicados
    /// independientemente de cuántas veces se ejecute.
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NotificacionPreexistente_NoSeDuplica()
    {
        return Prop.ForAll(
            IdempotencyArbitraries.DiasRestantesConEstadoTrial(),
            IdempotencyArbitraries.ComercioIdPositivo(),
            (diasRestantes, comercioId) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    var hoy = new DateOnly(2025, 6, 15);
                    var (comercio, suscripcion) = SeedTrialScenario(db, comercioId, diasRestantes, hoy);

                    // Determinar el tipo de notificación esperado
                    var tipoEsperado = diasRestantes switch
                    {
                        5 => "Trial por Expirar",
                        2 => "Trial Urgente",
                        _ => ""
                    };

                    // Pre-crear la notificación manualmente (simulando que ya fue procesada)
                    var notificacionExistente = new Notificacion
                    {
                        ComercioId = comercioId,
                        SuscripcionId = suscripcion.Id,
                        Titulo = tipoEsperado,
                        Mensaje = "Notificación preexistente",
                        TipoNotificacion = tipoEsperado,
                        FechaEmision = DateTime.UtcNow.AddHours(-1),
                        Leida = false
                    };
                    db.Notificaciones.Add(notificacionExistente);
                    db.SaveChanges();

                    var fechaProceso = hoy.ToDateTime(TimeOnly.MinValue);

                    // Ejecutar procesamiento: NO debe crear otra notificación del mismo tipo
                    service.ProcesarCortesDiariosAsync(fechaProceso).GetAwaiter().GetResult();

                    var countTotal = db.Notificaciones
                        .IgnoreQueryFilters()
                        .Count(n => n.SuscripcionId == suscripcion.Id
                                    && n.TipoNotificacion == tipoEsperado);

                    return (countTotal == 1)
                        .Label($"diasRestantes={diasRestantes}, tipo={tipoEsperado}, " +
                               $"countTotal={countTotal} (esperado: 1)");
                }
            });
    }
}
