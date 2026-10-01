using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Configuration;

/// <summary>
/// Tests for commerce configuration (ConfiguracionComercio) including defaults,
/// immediate effect of config changes, and role-based access control.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Configuration")]
public class CommerceConfigTests : IntegrationTestBase
{
    public CommerceConfigTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
        await SeedPlansAsync();
    }

    private async Task SeedPlansAsync()
    {
        await using var db = CreateDbContext();
        db.Set<Plan>().AddRange(
            new Plan { Id = 1, Nombre = "Básico", Precio = 350m, LimiteUsuarios = 2, LimiteAtributos = 2, LimiteSucursales = 1 },
            new Plan { Id = 2, Nombre = "Intermedio", Precio = 750m, LimiteUsuarios = 3, LimiteAtributos = 5, LimiteSucursales = 0 },
            new Plan { Id = 3, Nombre = "Empresarial", Precio = 1200m, LimiteUsuarios = 0, LimiteAtributos = 0, LimiteSucursales = 0 }
        );
        await db.SaveChangesAsync();

        // Seed SuperAdmin user (requerido por FK en LogsAuditoria.UsuarioId cuando
        // el endpoint POST /api/admin/comercios registra auditoría con UsuarioId=999)
        await SeedSuperAdminAsync();
    }

    [DockerAvailableFact]
    public async Task NewComercio_HasCorrectDefaultConfig()
    {
        // Arrange: Authenticate as SuperAdmin
        AuthenticateAs(999, 0, "SuperAdmin");

        var createRequest = new
        {
            Ruc = $"17{Guid.NewGuid().ToString("N")[..11]}",
            RazonSocial = "Comercio Config Test",
            PlanId = 3,
            UsaFacturacionSri = false
        };

        // Act: Create comercio via admin endpoint
        var response = await Client.PostAsJsonAsync("/api/admin/comercios", createRequest);

        // Assert: Comercio created successfully
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var comercioId = body.GetProperty("id").GetInt32();

        // Verify config defaults directly in DB
        await using var db = CreateDbContext();
        var config = await db.ConfiguracionesComercio
            .FirstOrDefaultAsync(c => c.ComercioId == comercioId);

        Assert.NotNull(config);
        Assert.False(config.EsBarEscolar);
        Assert.True(config.MostrarBotonCliente);
        Assert.False(config.PermiteVentaEnNegativo);
        Assert.False(config.ImpresionAutomaticaTicket);
    }

    [DockerAvailableFact]
    public async Task ConfigChange_HasImmediateEffect_OnNextRequest()
    {
        // Arrange: Create comercio with EsBarEscolar=false (default)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var cajero = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

        // Create ConfiguracionSucursal (el endpoint bar-escolar lee de esta tabla)
        var configSucursal = new ConfiguracionSucursal
        {
            SucursalId = sucursal.Id,
            EsBarEscolar = false,
            MostrarBotonCliente = true,
            PermiteVentaEnNegativo = false,
            ImpresionAutomaticaTicket = false,
            PermitePrecioNegociado = false
        };
        db.ConfiguracionesSucursal.Add(configSucursal);

        // Create a product for the bar-escolar request
        var producto = await TestDataBuilder.Producto()
            .EnComercio(comercio.Id).ConNombre("Producto Bar").ConPrecio(1.50m).CrearAsync(db);

        await db.SaveChangesAsync();

        // Authenticate as Cajero (CanSell policy)
        AuthenticateAs(cajero.Id, comercio.Id, "Cajero");

        var barRequest = new { SucursalId = sucursal.Id, ProductoId = producto.Id };

        // Act 1: Try bar-escolar with EsBarEscolar=false — should get 403 BAR_ESCOLAR_DISABLED
        var response1 = await Client.PostAsJsonAsync("/api/tenants/ventas/bar-escolar", barRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response1.StatusCode);

        var body1 = await response1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAR_ESCOLAR_DISABLED", body1.GetProperty("code").GetString());

        // Modify ConfiguracionesSucursal directly in DB: enable EsBarEscolar
        await using var db2 = CreateDbContext();
        var configToUpdate = await db2.ConfiguracionesSucursal
            .FirstAsync(c => c.SucursalId == sucursal.Id);
        configToUpdate.EsBarEscolar = true;
        await db2.SaveChangesAsync();

        // Act 2: Try bar-escolar again — should NOT get 403 BAR_ESCOLAR_DISABLED
        var response2 = await Client.PostAsJsonAsync("/api/tenants/ventas/bar-escolar", barRequest);

        // Assert: The response should not be 403 with BAR_ESCOLAR_DISABLED code
        // It might fail for other reasons (e.g., stock issues) but the config gate should pass
        if (response2.StatusCode == HttpStatusCode.Forbidden)
        {
            var body2 = await response2.Content.ReadFromJsonAsync<JsonElement>();
            Assert.NotEqual("BAR_ESCOLAR_DISABLED", body2.GetProperty("code").GetString());
        }
        // If not 403, the config change took immediate effect (success)
    }

    [DockerAvailableFact]
    public async Task Cajero_Gets403_OnManagementEndpoints()
    {
        // Arrange: Create comercio, sucursal, cajero
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var cajero = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

        // Authenticate as Cajero
        AuthenticateAs(cajero.Id, comercio.Id, "Cajero");

        // Act 1: Try POST /api/tenants/sucursales (CanManageSucursales = Gerente/Dueño only)
        var branchRequest = new { Nombre = "Sucursal Intrusa", Direccion = "Calle X", Telefono = "0999" };
        var response1 = await Client.PostAsJsonAsync("/api/tenants/sucursales", branchRequest);

        // Assert 1: Cajero should get 403
        Assert.Equal(HttpStatusCode.Forbidden, response1.StatusCode);

        // Act 2: Try POST /api/tenants/usuarios (CanManageUsers = Gerente/Dueño only)
        var userRequest = new
        {
            Nombre = "Usuario Intruso",
            Email = $"intruso-{Guid.NewGuid():N}@test.com",
            Password = "Password123!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };
        var response2 = await Client.PostAsJsonAsync("/api/tenants/usuarios", userRequest);

        // Assert 2: Cajero should get 403
        Assert.Equal(HttpStatusCode.Forbidden, response2.StatusCode);
    }
}
