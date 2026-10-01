using Microsoft.EntityFrameworkCore;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Integration tests verifying database-level uniqueness constraints,
/// cascades, and referential integrity using real PostgreSQL.
/// </summary>
[Collection("Postgres")]
public class UniquenessConstraintTests : IntegrationTestBase
{
    public UniquenessConstraintTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
    }

    [DockerAvailableFact]
    public async Task Comercio_DuplicateRuc_ThrowsUniqueConstraintViolation()
    {
        // Arrange
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);

        var ruc = "0991234567001";
        context.Comercios.Add(new Comercio
        {
            Ruc = ruc,
            RazonSocial = "Comercio Original",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Activo = true,
            FechaRegistro = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // Act & Assert: Attempt to insert duplicate RUC
        context.Comercios.Add(new Comercio
        {
            Ruc = ruc, // Same RUC!
            RazonSocial = "Comercio Duplicado",
            PlanId = plan.Id,
            UsaFacturacionSRI = false,
            Activo = true,
            FechaRegistro = DateTime.UtcNow
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());

        // PostgreSQL unique violation is wrapped in DbUpdateException
        Assert.Contains("ix_comercios_ruc", exception.InnerException?.Message ?? exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [DockerAvailableFact]
    public async Task Cliente_DuplicateComercioIdIdentificacion_ThrowsUniqueConstraintViolation()
    {
        // Arrange
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio = await SeedComercio(context, plan.Id, "0991111111001");

        var identificacion = "1712345678";
        context.Clientes.Add(new Cliente
        {
            ComercioId = comercio.Id,
            Identificacion = identificacion,
            Nombre = "Cliente Original",
            EsConsumidorFinal = false
        });
        await context.SaveChangesAsync();

        // Act & Assert: Attempt to insert duplicate (ComercioId, Identificacion)
        context.Clientes.Add(new Cliente
        {
            ComercioId = comercio.Id,
            Identificacion = identificacion, // Same identification in same comercio!
            Nombre = "Cliente Duplicado",
            EsConsumidorFinal = false
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());

        Assert.Contains("ix_clientes_comercio_identificacion", exception.InnerException?.Message ?? exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [DockerAvailableFact]
    public async Task Cliente_SameIdentificacionDifferentComercio_Succeeds()
    {
        // Arrange: Same identificacion should work in different comercios
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio1 = await SeedComercio(context, plan.Id, "0992222222001");
        var comercio2 = await SeedComercio(context, plan.Id, "0993333333001");

        var identificacion = "1712345678";
        context.Clientes.Add(new Cliente
        {
            ComercioId = comercio1.Id,
            Identificacion = identificacion,
            Nombre = "Cliente Comercio 1",
            EsConsumidorFinal = false
        });
        context.Clientes.Add(new Cliente
        {
            ComercioId = comercio2.Id,
            Identificacion = identificacion, // Same ID, different comercio = OK
            Nombre = "Cliente Comercio 2",
            EsConsumidorFinal = false
        });

        // Act & Assert: Should succeed without exception
        await context.SaveChangesAsync();

        var count = await context.Clientes.IgnoreQueryFilters().CountAsync(c => c.Identificacion == identificacion);
        Assert.Equal(2, count);
    }

    [DockerAvailableFact]
    public async Task Sucursal_Delete_SetsUsuarioSucursalIdToNull()
    {
        // Arrange
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio = await SeedComercio(context, plan.Id, "0994444444001");

        var sucursal = new Sucursal
        {
            ComercioId = comercio.Id,
            Nombre = "Sucursal a eliminar",
            Direccion = "Calle Test"
        };
        context.Sucursales.Add(sucursal);
        await context.SaveChangesAsync();

        var usuario = new Usuario
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            Nombre = "Usuario Asignado",
            Email = $"user-cascade-{Guid.NewGuid():N}@test.com",
            PasswordHash = "$2a$12$dummyhashforintegrationtests1234567890123456789",
            Rol = "Cajero",
            Activo = true
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        // Verify user has SucursalId set
        Assert.Equal(sucursal.Id, usuario.SucursalId);

        // Act: Delete the sucursal
        context.Sucursales.Remove(sucursal);
        await context.SaveChangesAsync();

        // Assert: User's SucursalId is set to NULL (ON DELETE SET NULL)
        await using var verifyContext = CreateDbContext();
        var updatedUsuario = await verifyContext.Usuarios
            .IgnoreQueryFilters()
            .FirstAsync(u => u.Id == usuario.Id);

        Assert.Null(updatedUsuario.SucursalId);
    }

    [DockerAvailableFact]
    public async Task Usuario_DuplicateEmail_ThrowsUniqueConstraintViolation()
    {
        // Arrange
        await using var context = CreateDbContext();
        var plan = await SeedPlan(context);
        var comercio = await SeedComercio(context, plan.Id, "0995555555001");

        var email = $"duplicate-{Guid.NewGuid():N}@test.com";
        context.Usuarios.Add(new Usuario
        {
            ComercioId = comercio.Id,
            Nombre = "User 1",
            Email = email,
            PasswordHash = "$2a$12$dummyhash",
            Rol = "Cajero",
            Activo = true
        });
        await context.SaveChangesAsync();

        // Act & Assert
        context.Usuarios.Add(new Usuario
        {
            ComercioId = comercio.Id,
            Nombre = "User 2",
            Email = email, // Duplicate email!
            PasswordHash = "$2a$12$dummyhash",
            Rol = "Cajero",
            Activo = true
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());

        Assert.Contains("ix_usuarios_email", exception.InnerException?.Message ?? exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Plan> SeedPlan(AppDbContext context)
    {
        var plan = new Plan
        {
            Nombre = "Plan Test",
            Precio = 30m,
            LimiteUsuarios = 10,
            LimiteAtributos = 5
        };
        context.Planes.Add(plan);
        await context.SaveChangesAsync();
        return plan;
    }

    private static async Task<Comercio> SeedComercio(AppDbContext context, int planId, string ruc)
    {
        var comercio = new Comercio
        {
            Ruc = ruc,
            RazonSocial = $"Comercio {ruc}",
            PlanId = planId,
            UsaFacturacionSRI = false,
            Activo = true,
            FechaRegistro = DateTime.UtcNow
        };
        context.Comercios.Add(comercio);
        await context.SaveChangesAsync();
        return comercio;
    }
}
