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

// Feature: prueba-gratuita, Property 15: Contadores de resumen de trials

/// <summary>
/// Property-based tests para los contadores de resumen de trials.
/// **Validates: Requirements 7.5**
///
/// Property 15: Contadores de resumen de trials.
/// Verificar que los contadores corresponden exactamente a count por estado y días restantes:
/// - TotalActivos = count(Estado == "Trial")
/// - PorExpirar = count(Estado == "Trial" AND diasRestantes &lt;= 5)
/// - Expirados = count(Estado == "Trial_Expirado")
/// </summary>
public class TrialResumenPropertyTests
{
    private static readonly Mock<ILogger<TrialService>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto de base de datos InMemory con advertencias de transacciones suprimidas
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
        var service = new TrialService(db, LoggerMock.Object);
        return (db, service);
    }

    /// <summary>
    /// Semilla el Plan Básico en la base de datos.
    /// </summary>
    private static Plan SeedPlanBasico(AppDbContext db)
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
        db.SaveChanges();
        return plan;
    }

    /// <summary>
    /// Crea un Comercio con un RUC único basado en el índice.
    /// </summary>
    private static Comercio CrearComercio(AppDbContext db, int planId, int index)
    {
        var comercio = new Comercio
        {
            Ruc = $"{index:D13}",
            RazonSocial = $"Comercio Test {index}",
            PlanId = planId,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow
        };
        db.Comercios.Add(comercio);
        db.SaveChanges();
        return comercio;
    }

    /// <summary>
    /// Crea una Suscripcion con el estado y fecha de próximo corte indicados.
    /// </summary>
    private static Suscripcion CrearSuscripcion(
        AppDbContext db, int comercioId, int planId, string estado, DateOnly fechaProximoCorte)
    {
        var suscripcion = new Suscripcion
        {
            ComercioId = comercioId,
            PlanId = planId,
            FechaInicio = fechaProximoCorte.AddDays(-15),
            FechaProximoCorte = fechaProximoCorte,
            MontoCuota = 0m,
            EsProporcional = false,
            Estado = estado,
            DiasExtendidos = 0
        };
        db.Suscripciones.Add(suscripcion);
        db.SaveChanges();
        return suscripcion;
    }

    // ─── Property 15: Contadores de resumen de trials ────────────────────────

    /// <summary>
    /// Para cantidades aleatorias de suscripciones Trial y Trial_Expirado,
    /// ObtenerResumenAsync().TotalActivos == count de suscripciones con Estado "Trial".
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Resumen_TotalActivos_CorrespondeACountTrial()
    {
        // Genera cantidades aleatorias: Trial (0-5), Trial con <=5 días (0-3), Trial_Expirado (0-3)
        var gen = from numTrialLejos in Gen.Choose(0, 5)
                  from numTrialCerca in Gen.Choose(0, 3)
                  from numExpirados in Gen.Choose(0, 3)
                  select (numTrialLejos, numTrialCerca, numExpirados);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (numTrialLejos, numTrialCerca, numExpirados) = tuple;
            var (db, service) = CreateContext();
            using (db)
            {
                var plan = SeedPlanBasico(db);
                var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
                var comercioIndex = 1;

                // Crear suscripciones Trial con > 5 días restantes
                for (int i = 0; i < numTrialLejos; i++)
                {
                    var comercio = CrearComercio(db, plan.Id, comercioIndex++);
                    CrearSuscripcion(db, comercio.Id, plan.Id, "Trial", hoy.AddDays(10));
                }

                // Crear suscripciones Trial con <= 5 días restantes
                for (int i = 0; i < numTrialCerca; i++)
                {
                    var comercio = CrearComercio(db, plan.Id, comercioIndex++);
                    CrearSuscripcion(db, comercio.Id, plan.Id, "Trial", hoy.AddDays(3));
                }

                // Crear suscripciones Trial_Expirado
                for (int i = 0; i < numExpirados; i++)
                {
                    var comercio = CrearComercio(db, plan.Id, comercioIndex++);
                    CrearSuscripcion(db, comercio.Id, plan.Id, "Trial_Expirado", hoy.AddDays(-5));
                }

                var resumen = service.ObtenerResumenAsync().GetAwaiter().GetResult();

                var expectedTotalActivos = numTrialLejos + numTrialCerca;
                return (resumen.TotalActivos == expectedTotalActivos)
                    .Label($"TotalActivos={resumen.TotalActivos}, esperado={expectedTotalActivos} " +
                           $"(Trial lejos={numTrialLejos}, Trial cerca={numTrialCerca}, Expirados={numExpirados})");
            }
        });
    }

    /// <summary>
    /// Para cantidades aleatorias de suscripciones Trial y Trial_Expirado,
    /// ObtenerResumenAsync().PorExpirar == count de suscripciones con Estado "Trial" y diasRestantes &lt;= 5.
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Resumen_PorExpirar_CorrespondeACountTrialConDiasRestantesMenorOIgual5()
    {
        var gen = from numTrialLejos in Gen.Choose(0, 5)
                  from numTrialCerca in Gen.Choose(0, 3)
                  from numExpirados in Gen.Choose(0, 3)
                  select (numTrialLejos, numTrialCerca, numExpirados);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (numTrialLejos, numTrialCerca, numExpirados) = tuple;
            var (db, service) = CreateContext();
            using (db)
            {
                var plan = SeedPlanBasico(db);
                var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
                var comercioIndex = 1;

                // Crear suscripciones Trial con > 5 días restantes (NO deben contar como PorExpirar)
                for (int i = 0; i < numTrialLejos; i++)
                {
                    var comercio = CrearComercio(db, plan.Id, comercioIndex++);
                    CrearSuscripcion(db, comercio.Id, plan.Id, "Trial", hoy.AddDays(10));
                }

                // Crear suscripciones Trial con <= 5 días restantes (SÍ deben contar como PorExpirar)
                for (int i = 0; i < numTrialCerca; i++)
                {
                    var comercio = CrearComercio(db, plan.Id, comercioIndex++);
                    CrearSuscripcion(db, comercio.Id, plan.Id, "Trial", hoy.AddDays(3));
                }

                // Crear suscripciones Trial_Expirado (NO deben contar como PorExpirar)
                for (int i = 0; i < numExpirados; i++)
                {
                    var comercio = CrearComercio(db, plan.Id, comercioIndex++);
                    CrearSuscripcion(db, comercio.Id, plan.Id, "Trial_Expirado", hoy.AddDays(-5));
                }

                var resumen = service.ObtenerResumenAsync().GetAwaiter().GetResult();

                return (resumen.PorExpirar == numTrialCerca)
                    .Label($"PorExpirar={resumen.PorExpirar}, esperado={numTrialCerca} " +
                           $"(Trial lejos={numTrialLejos}, Trial cerca={numTrialCerca}, Expirados={numExpirados})");
            }
        });
    }

    /// <summary>
    /// Para cantidades aleatorias de suscripciones Trial y Trial_Expirado,
    /// ObtenerResumenAsync().Expirados == count de suscripciones con Estado "Trial_Expirado".
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Resumen_Expirados_CorrespondeACountTrialExpirado()
    {
        var gen = from numTrialLejos in Gen.Choose(0, 5)
                  from numTrialCerca in Gen.Choose(0, 3)
                  from numExpirados in Gen.Choose(0, 3)
                  select (numTrialLejos, numTrialCerca, numExpirados);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (numTrialLejos, numTrialCerca, numExpirados) = tuple;
            var (db, service) = CreateContext();
            using (db)
            {
                var plan = SeedPlanBasico(db);
                var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
                var comercioIndex = 1;

                // Crear suscripciones Trial con > 5 días restantes
                for (int i = 0; i < numTrialLejos; i++)
                {
                    var comercio = CrearComercio(db, plan.Id, comercioIndex++);
                    CrearSuscripcion(db, comercio.Id, plan.Id, "Trial", hoy.AddDays(10));
                }

                // Crear suscripciones Trial con <= 5 días restantes
                for (int i = 0; i < numTrialCerca; i++)
                {
                    var comercio = CrearComercio(db, plan.Id, comercioIndex++);
                    CrearSuscripcion(db, comercio.Id, plan.Id, "Trial", hoy.AddDays(3));
                }

                // Crear suscripciones Trial_Expirado
                for (int i = 0; i < numExpirados; i++)
                {
                    var comercio = CrearComercio(db, plan.Id, comercioIndex++);
                    CrearSuscripcion(db, comercio.Id, plan.Id, "Trial_Expirado", hoy.AddDays(-5));
                }

                var resumen = service.ObtenerResumenAsync().GetAwaiter().GetResult();

                return (resumen.Expirados == numExpirados)
                    .Label($"Expirados={resumen.Expirados}, esperado={numExpirados} " +
                           $"(Trial lejos={numTrialLejos}, Trial cerca={numTrialCerca}, Expirados={numExpirados})");
            }
        });
    }
}
