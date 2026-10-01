using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Property-based tests para SubscriptionGuard.CanUseAIAsync.
/// **Validates: Requirements 4.3, 4.5**
///
/// Property 7: Feature gate deniega acceso a planes no-Empresarial y ante errores.
/// "For any comercio cuyo plan de suscripción NO sea 'Empresarial' (incluyendo 'Básico',
/// 'Intermedio', o cualquier plan futuro desconocido), y for any excepción que ocurra
/// durante la verificación del plan en base de datos, CanUseAIAsync SHALL retornar
/// false (fail-safe deny)."
/// </summary>
public class SubscriptionGuardAITests
{
    private static readonly Mock<ILogger<SubscriptionGuard>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto InMemory con ITenantContext configurado como SuperAdmin
    /// para evitar query filters.
    /// </summary>
    private static (AppDbContext db, SubscriptionGuard guard) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);
        var guard = new SubscriptionGuard(db, LoggerMock.Object);
        return (db, guard);
    }

    /// <summary>
    /// Property 7A: Para cualquier nombre de plan distinto de "Empresarial",
    /// CanUseAIAsync debe retornar false.
    /// Generamos nombres aleatorios y filtramos "Empresarial" con Prop.When.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanNoEmpresarial_CanUseAI_RetornaFalse()
    {
        // Generador: nombres de plan conocidos + strings aleatorios, excluyendo "Empresarial"
        var planNameGen = Gen.OneOf(
            Gen.Elements("Básico", "Intermedio", "Starter", "Premium", "Pro", "Free", "Trial"),
            Arb.Generate<NonEmptyString>().Select(s => s.Get)
        );

        return Prop.ForAll(planNameGen.ToArbitrary(), (string planName) =>
        {
            // Precondición: excluir "Empresarial"
            if (planName == "Empresarial")
                return true.ToProperty(); // Se descarta implícitamente vía filtro abajo

            var (db, guard) = CreateContext();
            using (db)
            {
                var plan = new Plan
                {
                    Id = 1,
                    Nombre = planName,
                    Precio = 100m,
                    LimiteUsuarios = 5,
                    LimiteAtributos = 5,
                    LimiteSucursales = 3
                };
                db.Planes.Add(plan);

                var comercio = new Comercio
                {
                    Id = 1,
                    Ruc = "0912345678001",
                    RazonSocial = "Test Comercio",
                    PlanId = 1,
                    Plan = plan,
                    Estado = "Activo",
                    FechaRegistro = DateTime.UtcNow
                };
                db.Comercios.Add(comercio);
                db.SaveChanges();

                // Act
                var result = guard.CanUseAIAsync(comercioId: 1).GetAwaiter().GetResult();

                // Assert — debe denegar acceso a IA para planes no-Empresarial
                return (!result).ToProperty()
                    .Label($"Plan '{planName}' debería ser denegado pero retornó true");
            }
        }).When(true); // Todas las iteraciones se evalúan (filtro dentro del lambda)
    }

    /// <summary>
    /// Property 7A alternativa con Prop.When explícito:
    /// Usa NonEmptyString de FsCheck para generar cualquier plan aleatorio.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanAleatorioNoEmpresarial_CanUseAI_RetornaFalse(NonEmptyString planNameGen)
    {
        var planName = planNameGen.Get;

        // Precondición: solo evaluar si el nombre no es "Empresarial"
        if (planName == "Empresarial")
            return true; // FsCheck descarta este caso (trivially true, no afecta la propiedad)

        var (db, guard) = CreateContext();
        using (db)
        {
            var plan = new Plan
            {
                Id = 1,
                Nombre = planName,
                Precio = 50m,
                LimiteUsuarios = 3,
                LimiteAtributos = 3,
                LimiteSucursales = 1
            };
            db.Planes.Add(plan);

            var comercio = new Comercio
            {
                Id = 1,
                Ruc = "0912345678001",
                RazonSocial = "Comercio Aleatorio",
                PlanId = 1,
                Plan = plan,
                Estado = "Activo",
                FechaRegistro = DateTime.UtcNow
            };
            db.Comercios.Add(comercio);
            db.SaveChanges();

            // Act
            var result = guard.CanUseAIAsync(comercioId: 1).GetAwaiter().GetResult();

            // Assert — false para cualquier plan que no sea "Empresarial"
            return result == false;
        }
    }

    /// <summary>
    /// Property 7B: Cuando el comercio no existe (comercioId inexistente),
    /// CanUseAIAsync retorna false (fail-safe por null).
    /// </summary>
    [Fact]
    public async Task ComercioInexistente_CanUseAI_RetornaFalse()
    {
        // Arrange — base de datos vacía, comercioId no existe
        var (db, guard) = CreateContext();
        using (db)
        {
            // Act
            var result = await guard.CanUseAIAsync(comercioId: 999);

            // Assert — fail-safe: deniega cuando el comercio no se encuentra
            Assert.False(result);
        }
    }

    /// <summary>
    /// Property 7C: Cuando ocurre una excepción durante la consulta a la base de datos,
    /// CanUseAIAsync retorna false (fail-safe deny).
    /// Simulamos disponiendo el DbContext antes de la consulta.
    /// </summary>
    [Fact]
    public async Task ExcepcionEnBaseDatos_CanUseAI_RetornaFalse()
    {
        // Arrange — disponemos el DbContext para forzar excepción al consultar
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);
        await db.DisposeAsync();

        // Creamos el guard con el contexto ya dispuesto — consultas lanzarán excepción
        var guard = new SubscriptionGuard(db, LoggerMock.Object);

        // Act
        var result = await guard.CanUseAIAsync(comercioId: 1);

        // Assert — fail-safe: deniega ante cualquier excepción
        Assert.False(result);
    }

    /// <summary>
    /// Property 7D: Cuando el comercio existe pero no tiene Plan asociado (null),
    /// CanUseAIAsync retorna false (fail-safe).
    /// </summary>
    [Fact]
    public async Task ComercioSinPlan_CanUseAI_RetornaFalse()
    {
        // Arrange
        var (db, guard) = CreateContext();
        using (db)
        {
            // Insertamos un comercio sin plan asociado (PlanId apunta a plan inexistente)
            var comercio = new Comercio
            {
                Id = 1,
                Ruc = "0912345678001",
                RazonSocial = "Test Comercio Sin Plan",
                PlanId = 999, // Plan inexistente
                Estado = "Activo",
                FechaRegistro = DateTime.UtcNow
            };
            db.Comercios.Add(comercio);
            await db.SaveChangesAsync();

            // Act
            var result = await guard.CanUseAIAsync(comercioId: 1);

            // Assert — fail-safe: Plan es null → deniega
            Assert.False(result);
        }
    }
}
