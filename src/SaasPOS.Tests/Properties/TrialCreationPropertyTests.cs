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

// Feature: prueba-gratuita, Property 1: Invariantes de creación de trial

/// <summary>
/// Property-based tests para los invariantes de creación de trial.
/// **Validates: Requirements 1.2, 1.3, 1.4**
///
/// Property 1: Invariantes de creación de trial.
/// "Para cualquier RUC válido (13 dígitos numéricos) y RazonSocial válida (1-200 chars)
/// sin historial previo de trial, la creación de un trial SHALL producir:
/// Comercio.Estado = 'Activo', Suscripcion.Estado = 'Trial',
/// Suscripcion.PlanId = Plan_Basico.Id, Suscripcion.FechaProximoCorte = FechaInicio + 15 días,
/// Suscripcion.MontoCuota = 0, y Suscripcion.EsProporcional = false."
/// </summary>
public class TrialCreationPropertyTests
{
    private static readonly Mock<ILogger<TrialService>> LoggerMock = new();

    /// <summary>
    /// Generadores personalizados para las propiedades de creación de trial.
    /// </summary>
    public static class TrialArbitraries
    {
        /// <summary>
        /// Genera un RUC válido: exactamente 13 dígitos numéricos.
        /// </summary>
        public static Arbitrary<string> ValidRuc()
        {
            var gen = Gen.Choose(0, 9)
                .ListOf(13)
                .Select(digits => new string(digits.Select(d => (char)('0' + d)).ToArray()));
            return gen.ToArbitrary();
        }

        /// <summary>
        /// Genera una RazonSocial válida: entre 1 y 200 caracteres, no solo espacios en blanco.
        /// </summary>
        public static Arbitrary<string> ValidRazonSocial()
        {
            // Genera strings alfanuméricos de 1-200 caracteres con al menos un carácter no-espacio
            var gen = from length in Gen.Choose(1, 200)
                      from chars in Gen.Elements(
                          'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
                          'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T',
                          'U', 'V', 'W', 'X', 'Y', 'Z', 'a', 'b', 'c', 'd',
                          'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n',
                          'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x',
                          'y', 'z', '0', '1', '2', '3', '4', '5', '6', '7',
                          '8', '9', ' ', '.', '-'
                      ).ListOf(length)
                      let str = new string(chars.ToArray())
                      where !string.IsNullOrWhiteSpace(str)
                      select str.Length > 200 ? str[..200] : str;
            return gen.ToArbitrary();
        }
    }

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

    // ─── Property 1: Invariantes de creación de trial ────────────────────────

    /// <summary>
    /// Para cualquier RUC válido y RazonSocial válida sin historial previo,
    /// CrearTrialAsync produce un Comercio con Estado "Activo".
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CrearTrial_ComercioEstadoActivo()
    {
        return Prop.ForAll(
            TrialArbitraries.ValidRuc(),
            TrialArbitraries.ValidRazonSocial(),
            (ruc, razonSocial) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    SeedPlanBasico(db);

                    var request = new CreateTrialRequest(ruc, razonSocial);
                    var result = service.CrearTrialAsync(request, 1).GetAwaiter().GetResult();

                    var comercio = db.Comercios
                        .IgnoreQueryFilters()
                        .First(c => c.Id == result.ComercioId);

                    return (comercio.Estado == "Activo").ToProperty();
                }
            });
    }

    /// <summary>
    /// Para cualquier RUC válido y RazonSocial válida sin historial previo,
    /// CrearTrialAsync produce una Suscripcion con Estado "Trial".
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CrearTrial_SuscripcionEstadoTrial()
    {
        return Prop.ForAll(
            TrialArbitraries.ValidRuc(),
            TrialArbitraries.ValidRazonSocial(),
            (ruc, razonSocial) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    SeedPlanBasico(db);

                    var request = new CreateTrialRequest(ruc, razonSocial);
                    var result = service.CrearTrialAsync(request, 1).GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == result.SuscripcionId);

                    return (suscripcion.Estado == "Trial").ToProperty();
                }
            });
    }

    /// <summary>
    /// Para cualquier RUC válido y RazonSocial válida sin historial previo,
    /// CrearTrialAsync asigna la Suscripcion al Plan Básico.
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CrearTrial_SuscripcionPlanBasico()
    {
        return Prop.ForAll(
            TrialArbitraries.ValidRuc(),
            TrialArbitraries.ValidRazonSocial(),
            (ruc, razonSocial) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    var planBasico = SeedPlanBasico(db);

                    var request = new CreateTrialRequest(ruc, razonSocial);
                    var result = service.CrearTrialAsync(request, 1).GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == result.SuscripcionId);

                    return (suscripcion.PlanId == planBasico.Id).ToProperty();
                }
            });
    }

    /// <summary>
    /// Para cualquier RUC válido y RazonSocial válida sin historial previo,
    /// CrearTrialAsync establece FechaProximoCorte = FechaInicio + 15 días.
    /// **Validates: Requirements 1.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CrearTrial_FechaProximoCorteEsFechaInicioMas15Dias()
    {
        return Prop.ForAll(
            TrialArbitraries.ValidRuc(),
            TrialArbitraries.ValidRazonSocial(),
            (ruc, razonSocial) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    SeedPlanBasico(db);

                    var request = new CreateTrialRequest(ruc, razonSocial);
                    var result = service.CrearTrialAsync(request, 1).GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == result.SuscripcionId);

                    var expectedCorte = suscripcion.FechaInicio.AddDays(15);
                    return (suscripcion.FechaProximoCorte == expectedCorte).ToProperty();
                }
            });
    }

    /// <summary>
    /// Para cualquier RUC válido y RazonSocial válida sin historial previo,
    /// CrearTrialAsync establece MontoCuota = 0.
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CrearTrial_MontoCuotaCero()
    {
        return Prop.ForAll(
            TrialArbitraries.ValidRuc(),
            TrialArbitraries.ValidRazonSocial(),
            (ruc, razonSocial) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    SeedPlanBasico(db);

                    var request = new CreateTrialRequest(ruc, razonSocial);
                    var result = service.CrearTrialAsync(request, 1).GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == result.SuscripcionId);

                    return (suscripcion.MontoCuota == 0m).ToProperty();
                }
            });
    }

    /// <summary>
    /// Para cualquier RUC válido y RazonSocial válida sin historial previo,
    /// CrearTrialAsync establece EsProporcional = false.
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CrearTrial_EsProporcionalFalse()
    {
        return Prop.ForAll(
            TrialArbitraries.ValidRuc(),
            TrialArbitraries.ValidRazonSocial(),
            (ruc, razonSocial) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    SeedPlanBasico(db);

                    var request = new CreateTrialRequest(ruc, razonSocial);
                    var result = service.CrearTrialAsync(request, 1).GetAwaiter().GetResult();

                    var suscripcion = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == result.SuscripcionId);

                    return (suscripcion.EsProporcional == false).ToProperty();
                }
            });
    }

    /// <summary>
    /// Propiedad compuesta: verifica TODOS los invariantes de creación de trial en una sola pasada.
    /// Para cualquier RUC válido y RazonSocial válida sin historial previo, CrearTrialAsync produce:
    /// - Comercio.Estado = "Activo"
    /// - Suscripcion.Estado = "Trial"
    /// - Suscripcion.PlanId = Plan_Basico.Id
    /// - Suscripcion.FechaProximoCorte = FechaInicio + 15 días
    /// - Suscripcion.MontoCuota = 0
    /// - Suscripcion.EsProporcional = false
    /// **Validates: Requirements 1.2, 1.3, 1.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CrearTrial_TodosLosInvariantesSeCumplen()
    {
        return Prop.ForAll(
            TrialArbitraries.ValidRuc(),
            TrialArbitraries.ValidRazonSocial(),
            (ruc, razonSocial) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    var planBasico = SeedPlanBasico(db);

                    var request = new CreateTrialRequest(ruc, razonSocial);
                    var result = service.CrearTrialAsync(request, 1).GetAwaiter().GetResult();

                    var comercio = db.Comercios
                        .IgnoreQueryFilters()
                        .First(c => c.Id == result.ComercioId);

                    var suscripcion = db.Suscripciones
                        .IgnoreQueryFilters()
                        .First(s => s.Id == result.SuscripcionId);

                    var comercioActivo = comercio.Estado == "Activo";
                    var estadoTrial = suscripcion.Estado == "Trial";
                    var planCorrecto = suscripcion.PlanId == planBasico.Id;
                    var fechaCorrecta = suscripcion.FechaProximoCorte == suscripcion.FechaInicio.AddDays(15);
                    var montoCero = suscripcion.MontoCuota == 0m;
                    var noProporcional = suscripcion.EsProporcional == false;

                    return (comercioActivo && estadoTrial && planCorrecto &&
                            fechaCorrecta && montoCero && noProporcional)
                        .Label($"Comercio.Estado={comercio.Estado}, " +
                               $"Suscripcion.Estado={suscripcion.Estado}, " +
                               $"PlanId={suscripcion.PlanId} (expected {planBasico.Id}), " +
                               $"FechaCorte={suscripcion.FechaProximoCorte} (expected {suscripcion.FechaInicio.AddDays(15)}), " +
                               $"MontoCuota={suscripcion.MontoCuota}, " +
                               $"EsProporcional={suscripcion.EsProporcional}");
                }
            });
    }
}
