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
/// Property-based tests for plan limit enforcement via SubscriptionGuard.
/// **Validates: Requirements 1.6**
///
/// Property 1: Plan limit enforcement rejects over-quota operations.
/// "For any commerce with an active plan and the maximum permitted number of users
/// (or branches) already created, attempting to add one more user (or branch) SHALL
/// result in rejection, regardless of which user initiates the request."
/// </summary>
public class PlanLimitPropertyTests
{
    private static readonly Mock<ILogger<SubscriptionGuard>> LoggerMock = new();

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

    private static Plan CreatePlanBasico() => new()
    {
        Id = 1,
        Nombre = "Básico",
        Precio = 9.99m,
        LimiteUsuarios = 2,
        LimiteAtributos = 2
    };

    private static Plan CreatePlanIntermedio() => new()
    {
        Id = 2,
        Nombre = "Intermedio",
        Precio = 29.99m,
        LimiteUsuarios = 3,
        LimiteAtributos = 5
    };

    private static Plan CreatePlanEmpresarial() => new()
    {
        Id = 3,
        Nombre = "Empresarial",
        Precio = 99.99m,
        LimiteUsuarios = 0, // unlimited
        LimiteAtributos = 0  // unlimited
    };

    private static Comercio CreateComercio(Plan plan, int comercioId = 1) => new()
    {
        Id = comercioId,
        Ruc = $"RUC{comercioId:D10}",
        RazonSocial = $"Comercio {comercioId}",
        PlanId = plan.Id,
        Plan = plan,
        FechaRegistro = DateTime.UtcNow
    };

    private static Usuario CreateUsuario(int comercioId, int? sucursalId = null) => new()
    {
        ComercioId = comercioId,
        SucursalId = sucursalId,
        Nombre = $"User-{Guid.NewGuid():N}",
        Email = $"{Guid.NewGuid():N}@test.com",
        PasswordHash = "hash",
        Rol = "Cajero",
        Activo = true
    };

    private static Sucursal CreateSucursal(int comercioId) => new()
    {
        ComercioId = comercioId,
        Nombre = $"Sucursal-{Guid.NewGuid():N}"
    };

    // ─── Property 1: User limit for Plan Básico ──────────────────────────────

    /// <summary>
    /// For Plan Básico with exactly 2 users already created (the maximum),
    /// CanAddUserAsync returns false — adding another user is rejected.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanBasico_AtUserLimit_RejectsNewUser(PositiveInt comercioIdGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var (db, guard) = CreateContext();
        using (db)
        {
            var plan = CreatePlanBasico();
            db.Planes.Add(plan);

            var comercio = CreateComercio(plan, comercioId);
            db.Comercios.Add(comercio);

            // Add exactly 2 users (the limit)
            db.Usuarios.Add(CreateUsuario(comercioId));
            db.Usuarios.Add(CreateUsuario(comercioId));
            db.SaveChanges();

            var result = guard.CanAddUserAsync(comercioId).GetAwaiter().GetResult();
            return result == false;
        }
    }

    /// <summary>
    /// For Plan Básico with fewer than 2 users (0 or 1), CanAddUserAsync returns true.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanBasico_BelowUserLimit_AllowsNewUser(PositiveInt comercioIdGen, bool addOneUser)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var (db, guard) = CreateContext();
        using (db)
        {
            var plan = CreatePlanBasico();
            db.Planes.Add(plan);

            var comercio = CreateComercio(plan, comercioId);
            db.Comercios.Add(comercio);

            // Add 0 or 1 users (below limit of 2)
            if (addOneUser)
            {
                db.Usuarios.Add(CreateUsuario(comercioId));
            }
            db.SaveChanges();

            var result = guard.CanAddUserAsync(comercioId).GetAwaiter().GetResult();
            return result == true;
        }
    }

    // ─── Property 2: Sucursal limit for Plan Básico ─────────────────────────

    /// <summary>
    /// For Plan Básico with 1 sucursal (the maximum), CanAddSucursalAsync returns false.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanBasico_AtSucursalLimit_RejectsNewSucursal(PositiveInt comercioIdGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var (db, guard) = CreateContext();
        using (db)
        {
            var plan = CreatePlanBasico();
            db.Planes.Add(plan);

            var comercio = CreateComercio(plan, comercioId);
            db.Comercios.Add(comercio);

            // Add 1 sucursal (the limit)
            db.Sucursales.Add(CreateSucursal(comercioId));
            db.SaveChanges();

            var result = guard.CanAddSucursalAsync(comercioId).GetAwaiter().GetResult();
            return result == false;
        }
    }

    /// <summary>
    /// For Plan Básico with 0 sucursales, CanAddSucursalAsync returns true.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanBasico_BelowSucursalLimit_AllowsNewSucursal(PositiveInt comercioIdGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var (db, guard) = CreateContext();
        using (db)
        {
            var plan = CreatePlanBasico();
            db.Planes.Add(plan);

            var comercio = CreateComercio(plan, comercioId);
            db.Comercios.Add(comercio);
            db.SaveChanges();

            var result = guard.CanAddSucursalAsync(comercioId).GetAwaiter().GetResult();
            return result == true;
        }
    }

    // ─── Property 3: User limit per sucursal for Plan Intermedio ────────────

    /// <summary>
    /// For Plan Intermedio with 3 users in a sucursal (the maximum per-sucursal),
    /// CanAddUserAsync(comercioId, sucursalId) returns false.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanIntermedio_AtUserLimitPerSucursal_RejectsNewUser(PositiveInt comercioIdGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var (db, guard) = CreateContext();
        using (db)
        {
            var plan = CreatePlanIntermedio();
            db.Planes.Add(plan);

            var comercio = CreateComercio(plan, comercioId);
            db.Comercios.Add(comercio);

            var sucursal = CreateSucursal(comercioId);
            db.Sucursales.Add(sucursal);
            db.SaveChanges();

            var sucursalId = sucursal.Id;

            // Add exactly 3 users in this sucursal (the limit)
            db.Usuarios.Add(CreateUsuario(comercioId, sucursalId));
            db.Usuarios.Add(CreateUsuario(comercioId, sucursalId));
            db.Usuarios.Add(CreateUsuario(comercioId, sucursalId));
            db.SaveChanges();

            var result = guard.CanAddUserAsync(comercioId, sucursalId).GetAwaiter().GetResult();
            return result == false;
        }
    }

    /// <summary>
    /// For Plan Intermedio with fewer than 3 users in a sucursal,
    /// CanAddUserAsync(comercioId, sucursalId) returns true.
    /// Uses FsCheck to generate random user counts 0-2.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanIntermedio_BelowUserLimitPerSucursal_AllowsNewUser(
        PositiveInt comercioIdGen,
        PositiveInt userCountGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var userCount = userCountGen.Get % 3; // 0, 1, or 2
        var (db, guard) = CreateContext();
        using (db)
        {
            var plan = CreatePlanIntermedio();
            db.Planes.Add(plan);

            var comercio = CreateComercio(plan, comercioId);
            db.Comercios.Add(comercio);

            var sucursal = CreateSucursal(comercioId);
            db.Sucursales.Add(sucursal);
            db.SaveChanges();

            var sucursalId = sucursal.Id;

            for (var i = 0; i < userCount; i++)
            {
                db.Usuarios.Add(CreateUsuario(comercioId, sucursalId));
            }
            db.SaveChanges();

            var result = guard.CanAddUserAsync(comercioId, sucursalId).GetAwaiter().GetResult();
            return result == true;
        }
    }

    // ─── Property 4: Empresarial always allows ──────────────────────────────

    /// <summary>
    /// For Plan Empresarial, CanAddUserAsync always returns true regardless of count.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanEmpresarial_AlwaysAllowsUsers(PositiveInt comercioIdGen, PositiveInt userCountGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var userCount = (userCountGen.Get % 20) + 1; // 1-20 users
        var (db, guard) = CreateContext();
        using (db)
        {
            var plan = CreatePlanEmpresarial();
            db.Planes.Add(plan);

            var comercio = CreateComercio(plan, comercioId);
            db.Comercios.Add(comercio);

            for (var i = 0; i < userCount; i++)
            {
                db.Usuarios.Add(CreateUsuario(comercioId));
            }
            db.SaveChanges();

            var result = guard.CanAddUserAsync(comercioId).GetAwaiter().GetResult();
            return result == true;
        }
    }

    /// <summary>
    /// For Plan Empresarial, CanAddSucursalAsync always returns true regardless of count.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PlanEmpresarial_AlwaysAllowsSucursales(PositiveInt comercioIdGen, PositiveInt sucursalCountGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var sucursalCount = (sucursalCountGen.Get % 10) + 1; // 1-10 sucursales
        var (db, guard) = CreateContext();
        using (db)
        {
            var plan = CreatePlanEmpresarial();
            db.Planes.Add(plan);

            var comercio = CreateComercio(plan, comercioId);
            db.Comercios.Add(comercio);

            for (var i = 0; i < sucursalCount; i++)
            {
                db.Sucursales.Add(CreateSucursal(comercioId));
            }
            db.SaveChanges();

            var result = guard.CanAddSucursalAsync(comercioId).GetAwaiter().GetResult();
            return result == true;
        }
    }

    // ─── Property 5: Fail-safe ──────────────────────────────────────────────

    /// <summary>
    /// If the comercio doesn't exist, CanAddUserAsync returns false (fail-safe).
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NonExistentComercio_CanAddUser_ReturnsFalse(PositiveInt comercioIdGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var (db, guard) = CreateContext();
        using (db)
        {
            // Don't add any comercio — it doesn't exist
            var result = guard.CanAddUserAsync(comercioId).GetAwaiter().GetResult();
            return result == false;
        }
    }

    /// <summary>
    /// If the comercio doesn't exist, CanAddSucursalAsync returns false (fail-safe).
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NonExistentComercio_CanAddSucursal_ReturnsFalse(PositiveInt comercioIdGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var (db, guard) = CreateContext();
        using (db)
        {
            var result = guard.CanAddSucursalAsync(comercioId).GetAwaiter().GetResult();
            return result == false;
        }
    }

    /// <summary>
    /// If the comercio exists but has no plan assigned (null navigation), returns false.
    /// We simulate this by creating a comercio with a PlanId that doesn't exist in Planes.
    /// Since InMemory doesn't enforce FK, Plan navigation will be null.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ComercioWithNullPlan_ReturnsFalse(PositiveInt comercioIdGen)
    {
        var comercioId = (comercioIdGen.Get % 1000) + 1;
        var (db, guard) = CreateContext();
        using (db)
        {
            // Create comercio with a PlanId that doesn't map to any Plan entity
            var comercio = new Comercio
            {
                Id = comercioId,
                Ruc = $"RUC{comercioId:D10}",
                RazonSocial = $"Comercio {comercioId}",
                PlanId = 9999, // non-existent plan
                FechaRegistro = DateTime.UtcNow
            };
            db.Comercios.Add(comercio);
            db.SaveChanges();

            var userResult = guard.CanAddUserAsync(comercioId).GetAwaiter().GetResult();
            var sucursalResult = guard.CanAddSucursalAsync(comercioId).GetAwaiter().GetResult();

            return userResult == false && sucursalResult == false;
        }
    }
}
