using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for dynamic attribute limit enforcement per plan tier.
/// **Validates: Requirements 6.3, 6.4**
/// </summary>
public class DynamicAttributeLimitPropertyTests
{
    private static AppDbContext CreateDbContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantContextMock.Object);
    }

    private static SubscriptionGuard CreateGuard(AppDbContext dbContext)
    {
        var loggerMock = new Mock<ILogger<SubscriptionGuard>>();
        return new SubscriptionGuard(dbContext, loggerMock.Object);
    }

    private static (Comercio comercio, Categoria categoria) SeedComercioWithPlan(
        AppDbContext dbContext, string planName, int limiteAtributos)
    {
        var plan = new Plan
        {
            Nombre = planName,
            Precio = 10m,
            LimiteUsuarios = 2,
            LimiteAtributos = limiteAtributos
        };
        dbContext.Planes.Add(plan);
        dbContext.SaveChanges();

        var comercio = new Comercio
        {
            Ruc = Guid.NewGuid().ToString("N")[..13],
            RazonSocial = "Test Comercio",
            PlanId = plan.Id,
            FechaRegistro = DateTime.UtcNow
        };
        dbContext.Comercios.Add(comercio);
        dbContext.SaveChanges();

        var categoria = new Categoria
        {
            ComercioId = comercio.Id,
            Nombre = "Test Categoria"
        };
        dbContext.Categorias.Add(categoria);
        dbContext.SaveChanges();

        return (comercio, categoria);
    }

    private static void SeedAttributes(AppDbContext dbContext, int categoriaId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            dbContext.AtributosCategoria.Add(new AtributoCategoria
            {
                CategoriaId = categoriaId,
                NombreAtributo = $"Attr_{i}",
                TipoDato = "Texto"
            });
        }
        dbContext.SaveChanges();
    }

    /// <summary>
    /// Property 7.1: Plan Básico with 2 existing attributes returns false (limit reached).
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Fact]
    public async Task PlanBasico_WithLimitReached_ReturnsFalse()
    {
        using var dbContext = CreateDbContext();
        var guard = CreateGuard(dbContext);
        var (comercio, categoria) = SeedComercioWithPlan(dbContext, "Básico", 2);
        SeedAttributes(dbContext, categoria.Id, 2);

        var result = await guard.CanAddAtributoCategoriaAsync(comercio.Id, categoria.Id);

        Assert.False(result);
    }

    /// <summary>
    /// Property 7.2: Plan Básico with 0 or 1 attributes returns true.
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanBasico_BelowLimit_ReturnsTrue(bool hasOneAttribute)
    {
        using var dbContext = CreateDbContext();
        var guard = CreateGuard(dbContext);
        var (comercio, categoria) = SeedComercioWithPlan(dbContext, "Básico", 2);

        int attributeCount = hasOneAttribute ? 1 : 0;
        SeedAttributes(dbContext, categoria.Id, attributeCount);

        var result = guard.CanAddAtributoCategoriaAsync(comercio.Id, categoria.Id)
            .GetAwaiter().GetResult();

        return result == true;
    }

    /// <summary>
    /// Property 7.3: Plan Intermedio with 5 existing attributes returns false (limit reached).
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Fact]
    public async Task PlanIntermedio_WithLimitReached_ReturnsFalse()
    {
        using var dbContext = CreateDbContext();
        var guard = CreateGuard(dbContext);
        var (comercio, categoria) = SeedComercioWithPlan(dbContext, "Intermedio", 5);
        SeedAttributes(dbContext, categoria.Id, 5);

        var result = await guard.CanAddAtributoCategoriaAsync(comercio.Id, categoria.Id);

        Assert.False(result);
    }

    /// <summary>
    /// Property 7.4: Plan Intermedio with 0-4 attributes returns true.
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanIntermedio_BelowLimit_ReturnsTrue(PositiveInt countGen)
    {
        // Generate attribute count in range 0-4
        int attributeCount = countGen.Get % 5; // 0..4

        using var dbContext = CreateDbContext();
        var guard = CreateGuard(dbContext);
        var (comercio, categoria) = SeedComercioWithPlan(dbContext, "Intermedio", 5);
        SeedAttributes(dbContext, categoria.Id, attributeCount);

        var result = guard.CanAddAtributoCategoriaAsync(comercio.Id, categoria.Id)
            .GetAwaiter().GetResult();

        return result == true;
    }

    /// <summary>
    /// Property 7.5: Plan Empresarial with any number of attributes always returns true (unlimited).
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanEmpresarial_AnyCount_AlwaysReturnsTrue(PositiveInt countGen)
    {
        // Generate a random number of existing attributes (1..50)
        int attributeCount = (countGen.Get % 50) + 1;

        using var dbContext = CreateDbContext();
        var guard = CreateGuard(dbContext);
        var (comercio, categoria) = SeedComercioWithPlan(dbContext, "Empresarial", 0);
        SeedAttributes(dbContext, categoria.Id, attributeCount);

        var result = guard.CanAddAtributoCategoriaAsync(comercio.Id, categoria.Id)
            .GetAwaiter().GetResult();

        return result == true;
    }

    /// <summary>
    /// Property 7.6: For a random number of existing attributes N and a plan with limit L:
    /// - If plan is Empresarial, always returns true
    /// - If N >= L, returns false
    /// - If N &lt; L, returns true
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AttributeLimit_EnforcedByPlanTier(PositiveInt attrCountGen, PositiveInt planIndexGen)
    {
        // Select plan tier based on generated index
        var plans = new[] {
            ("Básico", 2),
            ("Intermedio", 5),
            ("Empresarial", 0)
        };
        int planIdx = planIndexGen.Get % plans.Length;
        var (planName, limit) = plans[planIdx];

        // Generate attribute count in range 0..10
        int attributeCount = attrCountGen.Get % 11;

        using var dbContext = CreateDbContext();
        var guard = CreateGuard(dbContext);
        var (comercio, categoria) = SeedComercioWithPlan(dbContext, planName, limit);
        SeedAttributes(dbContext, categoria.Id, attributeCount);

        var result = guard.CanAddAtributoCategoriaAsync(comercio.Id, categoria.Id)
            .GetAwaiter().GetResult();

        // Determine expected result
        if (planName == "Empresarial")
            return result == true;

        if (attributeCount >= limit)
            return result == false;

        return result == true;
    }
}
