using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for multi-tenant isolation.
/// Verifies that ComercioId-based query filters enforce strict data isolation.
///
/// **Validates: Requirements 3.5, 16.1, 14.4**
/// </summary>
public class MultiTenantIsolationPropertyTests
{
    /// <summary>
    /// Simple test implementation of ITenantContext for controlling tenant isolation in tests.
    /// </summary>
    private sealed class TestTenantContext : ITenantContext
    {
        public int? ComercioId { get; set; }
        public int? SucursalId { get; set; }
        public bool IsSuperAdmin { get; set; }
    }

    /// <summary>
    /// Creates a fresh in-memory AppDbContext with the given tenant context.
    /// Each call uses a unique database name to avoid cross-test contamination.
    /// </summary>
    private static AppDbContext CreateDbContext(TestTenantContext tenantContext, string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options, tenantContext);
    }

    /// <summary>
    /// Seeds test data for multiple comercios into the database.
    /// Uses a SuperAdmin context to bypass query filters during seeding.
    /// </summary>
    private static string SeedMultiTenantData(int[] comercioIds)
    {
        var dbName = Guid.NewGuid().ToString();
        var superAdminContext = new TestTenantContext { ComercioId = null, IsSuperAdmin = true };

        using var db = CreateDbContext(superAdminContext, dbName);

        foreach (var comercioId in comercioIds)
        {
            // Seed Sucursal
            db.Sucursales.Add(new Sucursal
            {
                ComercioId = comercioId,
                Nombre = $"Sucursal-{comercioId}"
            });

            // Seed Producto
            db.Productos.Add(new Producto
            {
                ComercioId = comercioId,
                Nombre = $"Producto-{comercioId}",
                TipoArticulo = "Venta Directa"
            });

            // Seed Usuario
            db.Usuarios.Add(new Usuario
            {
                ComercioId = comercioId,
                Nombre = $"Usuario-{comercioId}",
                Email = $"user-{comercioId}@test.com",
                PasswordHash = "hashed",
                Rol = "Cajero"
            });

            // Seed Cliente
            db.Clientes.Add(new Cliente
            {
                ComercioId = comercioId,
                Identificacion = $"ID-{comercioId}",
                Nombre = $"Cliente-{comercioId}"
            });

            // Seed Venta (needs Sucursal and Usuario IDs - we'll use raw IDs)
            db.Ventas.Add(new Venta
            {
                ComercioId = comercioId,
                SucursalId = 0, // Will be set after SaveChanges
                UsuarioId = 0,  // Will be set after SaveChanges
                Total = 100m * comercioId,
                TipoComprobante = "Ticket Interno",
                FechaVenta = DateTime.UtcNow
            });

            // Seed Notificacion
            db.Notificaciones.Add(new Notificacion
            {
                ComercioId = comercioId,
                Titulo = $"Notificacion-{comercioId}",
                Mensaje = $"Stock bajo para comercio {comercioId}",
                TipoNotificacion = "StockBajo",
                FechaEmision = DateTime.UtcNow
            });
        }

        db.SaveChanges();

        // Now fix Venta references with actual saved Sucursal/Usuario IDs
        var ventas = db.Ventas.ToList();
        var sucursales = db.Sucursales.ToList();
        var usuarios = db.Usuarios.ToList();

        foreach (var venta in ventas)
        {
            var sucursal = sucursales.First(s => s.ComercioId == venta.ComercioId);
            var usuario = usuarios.First(u => u.ComercioId == venta.ComercioId);
            venta.SucursalId = sucursal.Id;
            venta.UsuarioId = usuario.Id;
        }

        db.SaveChanges();

        return dbName;
    }

    /// <summary>
    /// Property 5: For any authenticated request where the JWT's comercio_id claim is set,
    /// all data returned by the API SHALL belong exclusively to the ComercioId in the JWT claim.
    /// No entity with a different ComercioId SHALL appear in any response.
    ///
    /// This test verifies that querying Productos with a specific ComercioId tenant context
    /// returns ONLY productos belonging to that ComercioId.
    ///
    /// **Validates: Requirements 3.5, 16.1, 14.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Productos_OnlyReturnEntitiesForAuthenticatedComercio(PositiveInt seedGen)
    {
        // Generate 3 distinct comercio IDs
        var baseId = (seedGen.Get % 100) + 1;
        var comercioIds = new[] { baseId, baseId + 100, baseId + 200 };
        var targetComercioId = comercioIds[0];

        var dbName = SeedMultiTenantData(comercioIds);

        // Query with tenant context set to targetComercioId
        var tenantContext = new TestTenantContext { ComercioId = targetComercioId, IsSuperAdmin = false };
        using var db = CreateDbContext(tenantContext, dbName);

        var productos = db.Productos.ToList();

        // All returned productos must belong to the target comercio
        return productos.Count > 0
            && productos.All(p => p.ComercioId == targetComercioId);
    }

    /// <summary>
    /// Property 5: Verifies that Sucursales query isolation holds for any ComercioId.
    ///
    /// **Validates: Requirements 3.5, 16.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Sucursales_OnlyReturnEntitiesForAuthenticatedComercio(PositiveInt seedGen)
    {
        var baseId = (seedGen.Get % 100) + 1;
        var comercioIds = new[] { baseId, baseId + 100, baseId + 200 };
        var targetComercioId = comercioIds[0];

        var dbName = SeedMultiTenantData(comercioIds);

        var tenantContext = new TestTenantContext { ComercioId = targetComercioId, IsSuperAdmin = false };
        using var db = CreateDbContext(tenantContext, dbName);

        var sucursales = db.Sucursales.ToList();

        return sucursales.Count > 0
            && sucursales.All(s => s.ComercioId == targetComercioId);
    }

    /// <summary>
    /// Property 5: Verifies that Usuarios query isolation holds for any ComercioId.
    ///
    /// **Validates: Requirements 3.5, 16.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Usuarios_OnlyReturnEntitiesForAuthenticatedComercio(PositiveInt seedGen)
    {
        var baseId = (seedGen.Get % 100) + 1;
        var comercioIds = new[] { baseId, baseId + 100, baseId + 200 };
        var targetComercioId = comercioIds[0];

        var dbName = SeedMultiTenantData(comercioIds);

        var tenantContext = new TestTenantContext { ComercioId = targetComercioId, IsSuperAdmin = false };
        using var db = CreateDbContext(tenantContext, dbName);

        var usuarios = db.Usuarios.ToList();

        return usuarios.Count > 0
            && usuarios.All(u => u.ComercioId == targetComercioId);
    }

    /// <summary>
    /// Property 5: Verifies that Clientes query isolation holds for any ComercioId.
    ///
    /// **Validates: Requirements 3.5, 16.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Clientes_OnlyReturnEntitiesForAuthenticatedComercio(PositiveInt seedGen)
    {
        var baseId = (seedGen.Get % 100) + 1;
        var comercioIds = new[] { baseId, baseId + 100, baseId + 200 };
        var targetComercioId = comercioIds[0];

        var dbName = SeedMultiTenantData(comercioIds);

        var tenantContext = new TestTenantContext { ComercioId = targetComercioId, IsSuperAdmin = false };
        using var db = CreateDbContext(tenantContext, dbName);

        var clientes = db.Clientes.ToList();

        return clientes.Count > 0
            && clientes.All(c => c.ComercioId == targetComercioId);
    }

    /// <summary>
    /// Property 5: Verifies that Ventas query isolation holds for any ComercioId.
    ///
    /// **Validates: Requirements 3.5, 16.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Ventas_OnlyReturnEntitiesForAuthenticatedComercio(PositiveInt seedGen)
    {
        var baseId = (seedGen.Get % 100) + 1;
        var comercioIds = new[] { baseId, baseId + 100, baseId + 200 };
        var targetComercioId = comercioIds[0];

        var dbName = SeedMultiTenantData(comercioIds);

        var tenantContext = new TestTenantContext { ComercioId = targetComercioId, IsSuperAdmin = false };
        using var db = CreateDbContext(tenantContext, dbName);

        var ventas = db.Ventas.ToList();

        return ventas.Count > 0
            && ventas.All(v => v.ComercioId == targetComercioId);
    }

    /// <summary>
    /// Property 5: Verifies notification isolation (Req 14.4).
    /// Notifications for ComercioId=X are not visible to ComercioId=Y.
    ///
    /// **Validates: Requirements 14.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Notificaciones_OnlyReturnEntitiesForAuthenticatedComercio(PositiveInt seedGen)
    {
        var baseId = (seedGen.Get % 100) + 1;
        var comercioIds = new[] { baseId, baseId + 100, baseId + 200 };
        var targetComercioId = comercioIds[0];

        var dbName = SeedMultiTenantData(comercioIds);

        var tenantContext = new TestTenantContext { ComercioId = targetComercioId, IsSuperAdmin = false };
        using var db = CreateDbContext(tenantContext, dbName);

        var notificaciones = db.Notificaciones.ToList();

        return notificaciones.Count > 0
            && notificaciones.All(n => n.ComercioId == targetComercioId);
    }

    /// <summary>
    /// Property 5: SuperAdmin context (IsSuperAdmin=true) returns ALL entities across all comercios.
    /// Verifies that query filters are bypassed when IsSuperAdmin is true.
    ///
    /// **Validates: Requirements 3.5, 16.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SuperAdmin_ReturnsAllEntitiesAcrossAllComercios(PositiveInt seedGen)
    {
        var baseId = (seedGen.Get % 100) + 1;
        var comercioIds = new[] { baseId, baseId + 100, baseId + 200 };

        var dbName = SeedMultiTenantData(comercioIds);

        // Query with SuperAdmin context
        var superAdminContext = new TestTenantContext { ComercioId = null, IsSuperAdmin = true };
        using var db = CreateDbContext(superAdminContext, dbName);

        var productos = db.Productos.ToList();
        var sucursales = db.Sucursales.ToList();
        var usuarios = db.Usuarios.ToList();
        var clientes = db.Clientes.ToList();
        var ventas = db.Ventas.ToList();
        var notificaciones = db.Notificaciones.ToList();

        // SuperAdmin should see entities from ALL 3 comercios
        var productoComercios = productos.Select(p => p.ComercioId).Distinct().Count();
        var sucursalComercios = sucursales.Select(s => s.ComercioId).Distinct().Count();
        var usuarioComercios = usuarios.Select(u => u.ComercioId).Distinct().Count();
        var clienteComercios = clientes.Select(c => c.ComercioId).Distinct().Count();
        var ventaComercios = ventas.Select(v => v.ComercioId).Distinct().Count();
        var notificacionComercios = notificaciones.Select(n => n.ComercioId).Distinct().Count();

        return productoComercios == 3
            && sucursalComercios == 3
            && usuarioComercios == 3
            && clienteComercios == 3
            && ventaComercios == 3
            && notificacionComercios == 3;
    }

    /// <summary>
    /// Property 5: Cross-tenant notification isolation.
    /// Verifies that notifications for one comercio are never visible when querying as another comercio.
    ///
    /// **Validates: Requirements 14.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Notificaciones_CrossTenantIsolation_NoLeakage(PositiveInt seedGen)
    {
        var baseId = (seedGen.Get % 100) + 1;
        var comercioIds = new[] { baseId, baseId + 100, baseId + 200 };

        var dbName = SeedMultiTenantData(comercioIds);

        // Query as the SECOND comercio — should NOT see notifications from comercio 1 or 3
        var otherComercioId = comercioIds[1];
        var tenantContext = new TestTenantContext { ComercioId = otherComercioId, IsSuperAdmin = false };
        using var db = CreateDbContext(tenantContext, dbName);

        var notificaciones = db.Notificaciones.ToList();

        // Must only see notifications for otherComercioId, never for the other two
        return notificaciones.Count > 0
            && notificaciones.All(n => n.ComercioId == otherComercioId)
            && !notificaciones.Any(n => n.ComercioId == comercioIds[0])
            && !notificaciones.Any(n => n.ComercioId == comercioIds[2]);
    }
}
