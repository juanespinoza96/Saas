using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests para la validación de extensión de trial.
/// Feature: prueba-gratuita, Property 14: Validación de extensión
/// **Validates: Requirements 6.3, 6.4, 6.6**
///
/// Verifica rechazo para:
/// 1. Estados != "Trial" → EXTENSION_NO_APLICA
/// 2. Días fuera de [1,15] → DIAS_FUERA_DE_RANGO
/// 3. Acumulado > 30 → LIMITE_EXTENSION_SUPERADO
/// </summary>
public class TrialExtensionValidationPropertyTests
{
    private static readonly Mock<ILogger<TrialService>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto de base de datos InMemory con un nombre único y un TrialService configurado.
    /// </summary>
    private static (AppDbContext db, TrialService service) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);
        var service = new TrialService(db, LoggerMock.Object);
        return (db, service);
    }

    /// <summary>Crea el Plan Básico para seed de datos.</summary>
    private static Plan CreatePlanBasico() => new()
    {
        Id = 1,
        Nombre = "Básico",
        Precio = 350m,
        LimiteUsuarios = 2,
        LimiteAtributos = 2,
        LimiteSucursales = 1
    };

    /// <summary>Crea un Comercio asociado al Plan Básico.</summary>
    private static Comercio CreateComercio(int comercioId = 1) => new()
    {
        Id = comercioId,
        Ruc = "1234567890123",
        RazonSocial = "Comercio Test",
        PlanId = 1,
        Estado = "Activo",
        FechaRegistro = DateTime.UtcNow
    };

    /// <summary>Crea una Suscripcion con el estado y días extendidos indicados.</summary>
    private static Suscripcion CreateSuscripcion(int comercioId, string estado, int diasExtendidos = 0, int id = 1) => new()
    {
        Id = id,
        ComercioId = comercioId,
        PlanId = 1,
        FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow),
        FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
        MontoCuota = 0,
        EsProporcional = false,
        Estado = estado,
        DiasExtendidos = diasExtendidos
    };

    // ─── Propiedad 1: Estado no Trial → EXTENSION_NO_APLICA ─────────────────

    /// <summary>
    /// Para cualquier Suscripción con Estado distinto de "Trial",
    /// ExtenderTrialAsync lanza InvalidOperationException con mensaje "EXTENSION_NO_APLICA".
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EstadoNoTrialRechazaExtension()
    {
        // Generador de estados que no son "Trial"
        var estadoGen = Gen.Elements("Activa", "Por vencer", "En mora", "Suspendido", "Trial_Expirado");

        return Prop.ForAll(estadoGen.ToArbitrary(), estado =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                // Seed: Plan Básico + Comercio + Suscripción con estado no Trial
                db.Planes.Add(CreatePlanBasico());
                var comercio = CreateComercio();
                db.Comercios.Add(comercio);
                var suscripcion = CreateSuscripcion(comercio.Id, estado);
                db.Suscripciones.Add(suscripcion);
                db.SaveChanges();

                // Intentar extender con días válidos (5 días)
                var request = new ExtendTrialRequest(DiasAdicionales: 5);
                var ex = Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.ExtenderTrialAsync(suscripcion.Id, request, superAdminId: 1)
                ).GetAwaiter().GetResult();

                return (ex.Message == "EXTENSION_NO_APLICA").ToProperty();
            }
        });
    }

    // ─── Propiedad 2: Días fuera de rango [1,15] → DIAS_FUERA_DE_RANGO ──────

    /// <summary>
    /// Para una Suscripción con Estado "Trial" y DiasAdicionales fuera del rango [1, 15],
    /// ExtenderTrialAsync lanza InvalidOperationException con mensaje "DIAS_FUERA_DE_RANGO".
    /// **Validates: Requirements 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DiasFueraDeRango_Rechaza()
    {
        // Generador de días fuera de rango: menores a 1 o mayores a 15
        var diasGen = Gen.OneOf(
            Gen.Choose(-100, 0),   // Días <= 0
            Gen.Choose(16, 100)    // Días > 15
        );

        return Prop.ForAll(diasGen.ToArbitrary(), diasInvalidos =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                // Seed: Plan Básico + Comercio + Suscripción en estado Trial
                db.Planes.Add(CreatePlanBasico());
                var comercio = CreateComercio();
                db.Comercios.Add(comercio);
                var suscripcion = CreateSuscripcion(comercio.Id, "Trial");
                db.Suscripciones.Add(suscripcion);
                db.SaveChanges();

                // Intentar extender con días fuera de rango
                var request = new ExtendTrialRequest(DiasAdicionales: diasInvalidos);
                var ex = Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.ExtenderTrialAsync(suscripcion.Id, request, superAdminId: 1)
                ).GetAwaiter().GetResult();

                return (ex.Message == "DIAS_FUERA_DE_RANGO").ToProperty();
            }
        });
    }

    // ─── Propiedad 3: Acumulado excede 30 días → LIMITE_EXTENSION_SUPERADO ───

    /// <summary>
    /// Para una Suscripción con Estado "Trial" donde DiasExtendidos + DiasAdicionales > 30,
    /// ExtenderTrialAsync lanza InvalidOperationException con mensaje "LIMITE_EXTENSION_SUPERADO".
    /// **Validates: Requirements 6.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AcumuladoExcedeLimite_Rechaza()
    {
        // Generador: DiasExtendidos entre 16 y 29, DiasAdicionales entre 1 y 15,
        // tal que la suma siempre supere 30
        var gen = from diasExtendidos in Gen.Choose(16, 29)
                  from diasAdicionales in Gen.Choose(Math.Max(1, 31 - diasExtendidos), 15)
                  select (diasExtendidos, diasAdicionales);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (diasExtendidos, diasAdicionales) = tuple;
            var (db, service) = CreateContext();
            using (db)
            {
                // Seed: Plan Básico + Comercio + Suscripción en estado Trial con días ya extendidos
                db.Planes.Add(CreatePlanBasico());
                var comercio = CreateComercio();
                db.Comercios.Add(comercio);
                var suscripcion = CreateSuscripcion(comercio.Id, "Trial", diasExtendidos: diasExtendidos);
                db.Suscripciones.Add(suscripcion);
                db.SaveChanges();

                // Intentar extender con días que exceden el límite acumulado de 30
                var request = new ExtendTrialRequest(DiasAdicionales: diasAdicionales);
                var ex = Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.ExtenderTrialAsync(suscripcion.Id, request, superAdminId: 1)
                ).GetAwaiter().GetResult();

                return (ex.Message == "LIMITE_EXTENSION_SUPERADO").ToProperty();
            }
        });
    }
}
