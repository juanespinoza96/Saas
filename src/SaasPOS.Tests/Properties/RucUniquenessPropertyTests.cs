using System.Security.Claims;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SaasPOS.Api.Controllers.Admin;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;


namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for RUC uniqueness across all commerces.
/// **Validates: Requirements 2.2**
///
/// Property 2: RUC uniqueness across all commerces.
/// "For any two commerce registrations that share the same RUC value, the second
/// registration attempt SHALL fail with a conflict error, and no duplicate RUC
/// SHALL exist in the Comercios table."
/// </summary>
public class RucUniquenessPropertyTests
{
    private static (AppDbContext db, ComerciosController controller) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        var jtiBlocklistMock = new Mock<IJtiBlocklist>();
        var auditServiceMock = new Mock<IAuditService>();
        var billingServiceMock = new Mock<IBillingService>();
        var controller = new ComerciosController(db, jtiBlocklistMock.Object, auditServiceMock.Object, billingServiceMock.Object);

        // Set up a fake ClaimsPrincipal with required claims so GetUsuarioId() works
        var claims = new List<Claim>
        {
            new Claim("sub", "1"),
            new Claim("role", "SuperAdmin"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        return (db, controller);
    }

    private static Plan SeedDefaultPlan(AppDbContext db)
    {
        var plan = new Plan
        {
            Id = 1,
            Nombre = "Básico",
            Precio = 9.99m,
            LimiteUsuarios = 5,
            LimiteAtributos = 3
        };
        db.Planes.Add(plan);
        db.SaveChanges();
        return plan;
    }

    // ─── Property 1: First registration succeeds ─────────────────────────────

    /// <summary>
    /// A new RUC that doesn't exist in the DB can be registered successfully.
    /// The controller returns CreatedAtAction (201).
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FirstRegistration_WithNewRuc_Succeeds(NonEmptyString rucGen)
    {
        var ruc = rucGen.Get.Trim();
        if (string.IsNullOrWhiteSpace(ruc) || ruc.Length > 20) return true; // skip invalid

        var (db, controller) = CreateContext();
        using (db)
        {
            SeedDefaultPlan(db);

            var request = new CreateComercioRequest(ruc, "Comercio Test", 1);
            var result = controller.Create(request).GetAwaiter().GetResult();

            return result is CreatedAtActionResult;
        }
    }

    // ─── Property 2: Duplicate RUC rejected ──────────────────────────────────

    /// <summary>
    /// After inserting a Comercio with RUC "X", a second Comercio with the same RUC "X"
    /// is rejected with a Conflict (409) response.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool DuplicateRuc_IsRejected_WithConflict(NonEmptyString rucGen)
    {
        var ruc = rucGen.Get.Trim();
        if (string.IsNullOrWhiteSpace(ruc) || ruc.Length > 20) return true; // skip invalid

        var (db, controller) = CreateContext();
        using (db)
        {
            SeedDefaultPlan(db);

            // First registration succeeds
            var request1 = new CreateComercioRequest(ruc, "Comercio Uno", 1);
            var result1 = controller.Create(request1).GetAwaiter().GetResult();
            if (result1 is not CreatedAtActionResult) return false;

            // Second registration with same RUC fails with Conflict
            var request2 = new CreateComercioRequest(ruc, "Comercio Dos", 1);
            var result2 = controller.Create(request2).GetAwaiter().GetResult();

            return result2 is ConflictObjectResult;
        }
    }

    // ─── Property 3: Different RUCs succeed ──────────────────────────────────

    /// <summary>
    /// Two comercios with different RUCs can both be registered successfully.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool DifferentRucs_BothSucceed(NonEmptyString ruc1Gen, NonEmptyString ruc2Gen)
    {
        var ruc1 = ruc1Gen.Get.Trim();
        var ruc2 = ruc2Gen.Get.Trim();

        // Skip if either is invalid or if they happen to be equal
        if (string.IsNullOrWhiteSpace(ruc1) || ruc1.Length > 20) return true;
        if (string.IsNullOrWhiteSpace(ruc2) || ruc2.Length > 20) return true;
        if (ruc1 == ruc2) return true; // trivially true — duplicates tested separately

        var (db, controller) = CreateContext();
        using (db)
        {
            SeedDefaultPlan(db);

            var request1 = new CreateComercioRequest(ruc1, "Comercio Uno", 1);
            var result1 = controller.Create(request1).GetAwaiter().GetResult();

            var request2 = new CreateComercioRequest(ruc2, "Comercio Dos", 1);
            var result2 = controller.Create(request2).GetAwaiter().GetResult();

            return result1 is CreatedAtActionResult && result2 is CreatedAtActionResult;
        }
    }

    // ─── Property 4: RUC uniqueness invariant ────────────────────────────────

    /// <summary>
    /// At any point after multiple registrations (some duplicate, some not),
    /// no two Comercios in the table share the same RUC.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RucUniquenessInvariant_NoDuplicatesInTable(PositiveInt countGen)
    {
        var registrationCount = (countGen.Get % 10) + 2; // 2 to 11 attempts
        var (db, controller) = CreateContext();
        using (db)
        {
            SeedDefaultPlan(db);

            // Attempt several registrations, some with duplicate RUCs
            var rucs = new List<string>();
            for (var i = 0; i < registrationCount; i++)
            {
                // Alternate between new RUCs and repeating earlier ones
                string ruc;
                if (i > 0 && i % 3 == 0 && rucs.Count > 0)
                {
                    // Reuse an earlier RUC (should be rejected)
                    ruc = rucs[i % rucs.Count];
                }
                else
                {
                    ruc = $"{1000000000 + i}";
                    rucs.Add(ruc);
                }

                var request = new CreateComercioRequest(ruc, $"Comercio {i}", 1);
                controller.Create(request).GetAwaiter().GetResult();
            }

            // Invariant: no two comercios share the same RUC
            var allRucs = db.Comercios.Select(c => c.Ruc).ToList();
            var distinctRucs = allRucs.Distinct().ToList();

            return allRucs.Count == distinctRucs.Count;
        }
    }
}
