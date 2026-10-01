using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Audit;

/// <summary>
/// Tests that audit log access is restricted to SuperAdmin only.
/// Other roles (Gerente, Cajero, etc.) receive HTTP 403.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Audit")]
public class AuditAccessControlTests : IntegrationTestBase
{
    public AuditAccessControlTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
        await SeedPlansAsync();
        await SeedSuperAdminAsync();
    }

    private async Task SeedPlansAsync()
    {
        await using var db = CreateDbContext();
        db.Set<SaasPOS.Domain.Entities.Plan>().AddRange(
            new SaasPOS.Domain.Entities.Plan { Id = 1, Nombre = "Básico", Precio = 350m, LimiteUsuarios = 2, LimiteAtributos = 2, LimiteSucursales = 1 },
            new SaasPOS.Domain.Entities.Plan { Id = 2, Nombre = "Intermedio", Precio = 750m, LimiteUsuarios = 3, LimiteAtributos = 5, LimiteSucursales = 0 },
            new SaasPOS.Domain.Entities.Plan { Id = 3, Nombre = "Empresarial", Precio = 1200m, LimiteUsuarios = 0, LimiteAtributos = 0, LimiteSucursales = 0 }
        );
        await db.SaveChangesAsync();
    }

    // ── 15.3: SuperAdmin Can Read Audit Logs ──────────────────────────────────

    [DockerAvailableFact]
    public async Task SuperAdmin_CanReadAuditLogs()
    {
        // Arrange: Create some audit logs by creating a user via API
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Create a user to generate an audit log
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");
        var createUserRequest = new
        {
            Nombre = "Audit Test User",
            Email = $"audit-test-{Guid.NewGuid():N}@test.com",
            Password = "Password123!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };
        var createResponse = await Client.PostAsJsonAsync("/api/tenants/usuarios", createUserRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Act: Authenticate as SuperAdmin and query audit logs
        AuthenticateAs(999, 0, "SuperAdmin");
        var response = await Client.GetAsync("/api/admin/logs");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "SuperAdmin should see audit log items");
    }

    // ── 15.3: Gerente Gets 403 on Audit Logs ─────────────────────────────────

    [DockerAvailableFact]
    public async Task Gerente_Gets403_OnAuditLogs()
    {
        // Arrange
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Act: Authenticate as Gerente and try to access audit logs
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");
        var response = await Client.GetAsync("/api/admin/logs");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 15.3: Cajero Gets 403 on Audit Logs ──────────────────────────────────

    [DockerAvailableFact]
    public async Task Cajero_Gets403_OnAuditLogs()
    {
        // Arrange
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var cajero = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

        // Act: Authenticate as Cajero and try to access audit logs
        AuthenticateAs(cajero.Id, comercio.Id, "Cajero");
        var response = await Client.GetAsync("/api/admin/logs");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
