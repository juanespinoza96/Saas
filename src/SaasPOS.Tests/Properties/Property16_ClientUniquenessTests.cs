using System.Security.Claims;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SaasPOS.Api.Controllers.Tenants;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for client uniqueness within a commerce.
/// **Validates: Requirements 12.1**
///
/// Property 16: Client uniqueness within a commerce.
/// "For any commerce, attempting to register two clients with the same Identificacion
/// SHALL result in the second attempt being rejected with a conflict error, and the
/// (ComercioId, Identificacion) pair SHALL remain unique in the Clientes table."
/// </summary>
public class Property16_ClientUniquenessTests
{
    private static (AppDbContext db, ClientesController controller) CreateContext(int comercioId = 1)
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.ComercioId).Returns(comercioId);
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(false);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        var auditServiceMock = new Mock<IAuditService>();
        var controller = new ClientesController(db, tenantContextMock.Object, auditServiceMock.Object);

        // Set up a fake ClaimsPrincipal with required claims so GetUsuarioId() works
        var claims = new List<Claim>
        {
            new Claim("sub", "1"),
            new Claim("comercio_id", comercioId.ToString()),
            new Claim("role", "Cajero"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        return (db, controller);
    }

    private static (AppDbContext db, ClientesController controller1, ClientesController controller2)
        CreateDualCommerceContext(int comercioId1 = 1, int comercioId2 = 2)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Shared DB, two different tenant contexts
        var tenantMock1 = new Mock<ITenantContext>();
        tenantMock1.Setup(t => t.ComercioId).Returns(comercioId1);
        tenantMock1.Setup(t => t.IsSuperAdmin).Returns(false);

        var tenantMock2 = new Mock<ITenantContext>();
        tenantMock2.Setup(t => t.ComercioId).Returns(comercioId2);
        tenantMock2.Setup(t => t.IsSuperAdmin).Returns(false);

        var db = new AppDbContext(options, tenantMock1.Object);

        var auditServiceMock = new Mock<IAuditService>();
        var controller1 = new ClientesController(db, tenantMock1.Object, auditServiceMock.Object);
        var controller2 = new ClientesController(db, tenantMock2.Object, auditServiceMock.Object);

        // Set up fake ClaimsPrincipal for controller1
        var claims1 = new List<Claim>
        {
            new Claim("sub", "1"),
            new Claim("comercio_id", comercioId1.ToString()),
            new Claim("role", "Cajero"),
        };
        var identity1 = new ClaimsIdentity(claims1, "TestAuth");
        var principal1 = new ClaimsPrincipal(identity1);
        controller1.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal1 }
        };

        // Set up fake ClaimsPrincipal for controller2
        var claims2 = new List<Claim>
        {
            new Claim("sub", "2"),
            new Claim("comercio_id", comercioId2.ToString()),
            new Claim("role", "Cajero"),
        };
        var identity2 = new ClaimsIdentity(claims2, "TestAuth");
        var principal2 = new ClaimsPrincipal(identity2);
        controller2.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal2 }
        };

        return (db, controller1, controller2);
    }

    private static void SeedComercio(AppDbContext db, int comercioId)
    {
        if (!db.Planes.Any(p => p.Id == 1))
        {
            db.Planes.Add(new Plan
            {
                Id = 1,
                Nombre = "Básico",
                Precio = 9.99m,
                LimiteUsuarios = 5,
                LimiteAtributos = 3
            });
        }

        if (!db.Comercios.Any(c => c.Id == comercioId))
        {
            db.Comercios.Add(new Comercio
            {
                Id = comercioId,
                Ruc = $"RUC-{comercioId}",
                RazonSocial = $"Comercio {comercioId}",
                PlanId = 1,
                Activo = true,
                FechaRegistro = DateTime.UtcNow
            });
        }

        db.SaveChanges();
    }

    private static CreateClienteRequest MakeRequest(string identificacion) =>
        new(identificacion, "Cliente Test", null, null, null, EsConsumidorFinal: true);

    // ─── Property 16.1: First registration succeeds ──────────────────────────

    /// <summary>
    /// A new client with any Identificacion that doesn't exist in the commerce
    /// can be registered successfully (201 Created).
    /// **Validates: Requirements 12.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FirstClientRegistration_WithNewIdentificacion_Succeeds(NonEmptyString idGen)
    {
        var identificacion = idGen.Get.Trim();
        if (string.IsNullOrWhiteSpace(identificacion)) return true; // skip invalid

        var (db, controller) = CreateContext();
        using (db)
        {
            SeedComercio(db, 1);

            var request = MakeRequest(identificacion);
            var result = controller.Create(request).GetAwaiter().GetResult();

            return result is CreatedAtActionResult;
        }
    }

    // ─── Property 16.2: Duplicate Identificacion in same commerce rejected ───

    /// <summary>
    /// After inserting a client with Identificacion "X" in a commerce, a second
    /// client with the same Identificacion in the SAME commerce is rejected
    /// with Conflict (409).
    /// **Validates: Requirements 12.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool DuplicateIdentificacion_InSameCommerce_IsRejectedWithConflict(NonEmptyString idGen)
    {
        var identificacion = idGen.Get.Trim();
        if (string.IsNullOrWhiteSpace(identificacion)) return true; // skip invalid

        var (db, controller) = CreateContext();
        using (db)
        {
            SeedComercio(db, 1);

            // First registration succeeds
            var request1 = MakeRequest(identificacion);
            var result1 = controller.Create(request1).GetAwaiter().GetResult();
            if (result1 is not CreatedAtActionResult) return false;

            // Second registration with same Identificacion fails with Conflict
            var request2 = MakeRequest(identificacion);
            var result2 = controller.Create(request2).GetAwaiter().GetResult();

            return result2 is ConflictObjectResult;
        }
    }

    // ─── Property 16.3: Same Identificacion in different commerces succeeds ──

    /// <summary>
    /// Two clients with the SAME Identificacion in DIFFERENT commerces can both
    /// be registered successfully. Uniqueness is per-commerce, not global.
    /// **Validates: Requirements 12.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SameIdentificacion_InDifferentCommerces_BothSucceed(NonEmptyString idGen)
    {
        var identificacion = idGen.Get.Trim();
        if (string.IsNullOrWhiteSpace(identificacion)) return true; // skip invalid

        var (db, controller1, controller2) = CreateDualCommerceContext(1, 2);
        using (db)
        {
            SeedComercio(db, 1);
            SeedComercio(db, 2);

            // Register same Identificacion in commerce 1
            var request1 = MakeRequest(identificacion);
            var result1 = controller1.Create(request1).GetAwaiter().GetResult();

            // Register same Identificacion in commerce 2
            var request2 = MakeRequest(identificacion);
            var result2 = controller2.Create(request2).GetAwaiter().GetResult();

            return result1 is CreatedAtActionResult && result2 is CreatedAtActionResult;
        }
    }

    // ─── Property 16.4: Uniqueness invariant after multiple registrations ────

    /// <summary>
    /// After multiple registrations (some duplicate, some not), no two Clientes
    /// in the same ComercioId share the same Identificacion.
    /// **Validates: Requirements 12.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ClientUniquenessInvariant_NoDuplicatesPerCommerce(PositiveInt countGen)
    {
        var registrationCount = (countGen.Get % 10) + 2; // 2 to 11 attempts
        var (db, controller) = CreateContext();
        using (db)
        {
            SeedComercio(db, 1);

            var identificaciones = new List<string>();
            for (var i = 0; i < registrationCount; i++)
            {
                string identificacion;
                if (i > 0 && i % 3 == 0 && identificaciones.Count > 0)
                {
                    // Reuse an earlier Identificacion (should be rejected as duplicate)
                    identificacion = identificaciones[i % identificaciones.Count];
                }
                else
                {
                    identificacion = $"ID-{1000000 + i}";
                    identificaciones.Add(identificacion);
                }

                var request = MakeRequest(identificacion);
                controller.Create(request).GetAwaiter().GetResult();
            }

            // Invariant: no two clients in the same commerce share the same Identificacion
            var allClients = db.Clientes
                .Where(c => c.ComercioId == 1)
                .Select(c => c.Identificacion)
                .ToList();

            var distinctIdentificaciones = allClients.Distinct().ToList();

            return allClients.Count == distinctIdentificaciones.Count;
        }
    }
}
