using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

// Feature: prueba-gratuita, Property 5: Cálculo de días restantes

/// <summary>
/// Property-based tests para el cálculo de días restantes en trials.
/// Verifica que para cualquier par (FechaProximoCorte, fechaActual),
/// diasRestantes = max(0, FechaProximoCorte - fechaActual).
/// **Validates: Requirements 2.5**
/// </summary>
public class TrialDiasRestantesPropertyTests
{
    private static readonly Mock<ILogger<TrialService>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto de base de datos InMemory con las advertencias de transacciones suprimidas
    /// y un TrialService listo para usar.
    /// </summary>
    private static (AppDbContext db, TrialService service) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        // Sembrar el Plan Básico
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

        var service = new TrialService(db, LoggerMock.Object);
        return (db, service);
    }

    /// <summary>
    /// Crea un Comercio y Suscripción con la FechaProximoCorte y estado especificados.
    /// </summary>
    private static void SeedComercioConSuscripcion(AppDbContext db, DateOnly fechaProximoCorte, string estado)
    {
        // Generar un RUC único usando un GUID para evitar colisiones
        var ruc = Guid.NewGuid().ToString("N")[..13];

        var comercio = new Comercio
        {
            Ruc = ruc,
            RazonSocial = "Comercio Test DiasRestantes",
            PlanId = 1,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow.AddDays(-30)
        };

        db.Comercios.Add(comercio);
        db.SaveChanges();

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = 1,
            FechaInicio = fechaProximoCorte.AddDays(-15),
            FechaProximoCorte = fechaProximoCorte,
            MontoCuota = 0,
            EsProporcional = false,
            Estado = estado,
            DiasExtendidos = 0
        };

        db.Suscripciones.Add(suscripcion);
        db.SaveChanges();
    }

    // ─── Propiedad 5: Cálculo de días restantes ──────────────────────────────

    /// <summary>
    /// Para cualquier offset de días [-30, +30] desde hoy y estado "Trial",
    /// DiasRestantes = Math.Max(0, FechaProximoCorte.DayNumber - hoy.DayNumber).
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DiasRestantes_CalculoCorrecto_EstadoTrial()
    {
        return Prop.ForAll(
            Gen.Choose(-30, 30).ToArbitrary(),
            (int offsetDias) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
                    var fechaProximoCorte = hoy.AddDays(offsetDias);

                    SeedComercioConSuscripcion(db, fechaProximoCorte, "Trial");

                    var trials = service.ObtenerTrialsAsync().GetAwaiter().GetResult();

                    var trial = trials.First();
                    var esperado = Math.Max(0, fechaProximoCorte.DayNumber - hoy.DayNumber);

                    return (trial.DiasRestantes == esperado)
                        .Label($"Offset={offsetDias}, FechaCorte={fechaProximoCorte}, " +
                               $"Hoy={hoy}, DiasRestantes={trial.DiasRestantes}, Esperado={esperado}");
                }
            });
    }

    /// <summary>
    /// Para cualquier offset de días [-30, +30] desde hoy y estado "Trial_Expirado",
    /// DiasRestantes = Math.Max(0, FechaProximoCorte.DayNumber - hoy.DayNumber).
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DiasRestantes_CalculoCorrecto_EstadoTrialExpirado()
    {
        return Prop.ForAll(
            Gen.Choose(-30, 30).ToArbitrary(),
            (int offsetDias) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
                    var fechaProximoCorte = hoy.AddDays(offsetDias);

                    SeedComercioConSuscripcion(db, fechaProximoCorte, "Trial_Expirado");

                    var trials = service.ObtenerTrialsAsync().GetAwaiter().GetResult();

                    var trial = trials.First();
                    var esperado = Math.Max(0, fechaProximoCorte.DayNumber - hoy.DayNumber);

                    return (trial.DiasRestantes == esperado)
                        .Label($"Offset={offsetDias}, FechaCorte={fechaProximoCorte}, " +
                               $"Hoy={hoy}, DiasRestantes={trial.DiasRestantes}, Esperado={esperado}");
                }
            });
    }
}
