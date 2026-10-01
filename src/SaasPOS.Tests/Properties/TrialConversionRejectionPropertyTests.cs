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
/// Property-based tests para el rechazo de conversión inválida de trial.
/// Feature: prueba-gratuita, Property 12: Rechazo de conversión inválida
/// **Validates: Requirements 5.6, 5.7**
///
/// Verifica que estados distintos a Trial/Trial_Expirado retornan CONVERSION_NO_APLICA
/// y PlanId inexistente retorna PLAN_INVALIDO.
/// </summary>
public class TrialConversionRejectionPropertyTests
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

    /// <summary>Crea una Suscripcion con el estado indicado.</summary>
    private static Suscripcion CreateSuscripcion(int comercioId, string estado, int id = 1) => new()
    {
        Id = id,
        ComercioId = comercioId,
        PlanId = 1,
        FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow),
        FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
        MontoCuota = 0,
        EsProporcional = false,
        Estado = estado,
        DiasExtendidos = 0
    };

    // ─── Propiedad 1: Estado no convertible → CONVERSION_NO_APLICA ──────────

    /// <summary>
    /// Para cualquier Suscripción con Estado distinto de "Trial" o "Trial_Expirado",
    /// ConvertirTrialAsync lanza InvalidOperationException con mensaje "CONVERSION_NO_APLICA".
    /// **Validates: Requirements 5.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EstadoNoConvertible_RetornaConversionNoAplica()
    {
        // Generador de estados no convertibles
        var estadoGen = Gen.Elements("Activa", "Por vencer", "En mora", "Suspendido");

        return Prop.ForAll(estadoGen.ToArbitrary(), estado =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                // Seed: Plan Básico + Comercio + Suscripción con estado no convertible
                db.Planes.Add(CreatePlanBasico());
                var comercio = CreateComercio();
                db.Comercios.Add(comercio);
                var suscripcion = CreateSuscripcion(comercio.Id, estado);
                db.Suscripciones.Add(suscripcion);
                db.SaveChanges();

                // Intentar convertir con un plan válido (Id=1 existe)
                var request = new ConvertTrialRequest(PlanId: 1);
                var ex = Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.ConvertirTrialAsync(suscripcion.Id, request, superAdminId: 1)
                ).GetAwaiter().GetResult();

                return (ex.Message == "CONVERSION_NO_APLICA").ToProperty();
            }
        });
    }

    // ─── Propiedad 2: PlanId inexistente → PLAN_INVALIDO ────────────────────

    /// <summary>
    /// Para una Suscripción con Estado "Trial" y un PlanId que no existe en la tabla Planes,
    /// ConvertirTrialAsync lanza InvalidOperationException con mensaje "PLAN_INVALIDO".
    /// **Validates: Requirements 5.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanIdInexistente_RetornaPlanInvalido()
    {
        // Generador de PlanId inexistente (100 a 999, ninguno existe en la tabla seeded)
        var planIdGen = Gen.Choose(100, 999);

        return Prop.ForAll(planIdGen.ToArbitrary(), planIdInvalido =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                // Seed: Plan Básico (Id=1) + Comercio + Suscripción en estado Trial
                db.Planes.Add(CreatePlanBasico());
                var comercio = CreateComercio();
                db.Comercios.Add(comercio);
                var suscripcion = CreateSuscripcion(comercio.Id, "Trial");
                db.Suscripciones.Add(suscripcion);
                db.SaveChanges();

                // Intentar convertir con un PlanId que no existe
                var request = new ConvertTrialRequest(PlanId: planIdInvalido);
                var ex = Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.ConvertirTrialAsync(suscripcion.Id, request, superAdminId: 1)
                ).GetAwaiter().GetResult();

                return (ex.Message == "PLAN_INVALIDO").ToProperty();
            }
        });
    }

    // ─── Propiedad 3: SuscripcionId inexistente → CONVERSION_NO_APLICA ──────

    /// <summary>
    /// Para un suscripcionId que no existe en la base de datos,
    /// ConvertirTrialAsync lanza InvalidOperationException con mensaje "CONVERSION_NO_APLICA".
    /// **Validates: Requirements 5.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SuscripcionIdInexistente_RetornaConversionNoAplica()
    {
        // Generador de IDs de suscripción inexistentes (ninguna suscripción será creada)
        var idGen = Gen.Choose(100, 999);

        return Prop.ForAll(idGen.ToArbitrary(), suscripcionId =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                // Seed: Solo Plan Básico (sin comercio ni suscripción)
                db.Planes.Add(CreatePlanBasico());
                db.SaveChanges();

                // Intentar convertir una suscripción que no existe
                var request = new ConvertTrialRequest(PlanId: 1);
                var ex = Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.ConvertirTrialAsync(suscripcionId, request, superAdminId: 1)
                ).GetAwaiter().GetResult();

                return (ex.Message == "CONVERSION_NO_APLICA").ToProperty();
            }
        });
    }
}
