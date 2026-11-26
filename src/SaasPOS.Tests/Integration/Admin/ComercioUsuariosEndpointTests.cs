using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Admin;

/// <summary>
/// GET /api/admin/comercios/{id}/usuarios: lista los usuarios de un comercio para el panel SuperAdmin
/// (pestaña "Usuarios" de ComercioDetallePage). Solo SuperAdmin, sin hash de contraseña.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Admin")]
public class ComercioUsuariosEndpointTests : IntegrationTestBase
{
    public ComercioUsuariosEndpointTests(PostgresFixture fixture) : base(fixture) { }

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

    [DockerAvailableFact]
    public async Task SuperAdmin_RecibeSoloLosUsuariosDelComercioPedido()
    {
        await using var db = CreateDbContext();
        var comercioA = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var comercioB = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursalA = await TestDataBuilder.Sucursal().EnComercio(comercioA.Id).CrearAsync(db);
        var sucursalB = await TestDataBuilder.Sucursal().EnComercio(comercioB.Id).CrearAsync(db);

        var duenoA = await TestDataBuilder.Usuario().EnComercio(comercioA.Id).EnSucursal(sucursalA.Id)
            .ConRol("Dueño").ConNombre("Dueño A").ConEmail($"dueno-a-{Guid.NewGuid():N}@test.com").CrearAsync(db);
        var cajeroA = await TestDataBuilder.Usuario().EnComercio(comercioA.Id).EnSucursal(sucursalA.Id)
            .ConRol("Cajero").ConNombre("Cajero A").ConEmail($"cajero-a-{Guid.NewGuid():N}@test.com").Inactivo().CrearAsync(db);
        var usuarioB = await TestDataBuilder.Usuario().EnComercio(comercioB.Id).EnSucursal(sucursalB.Id)
            .ConRol("Gerente").ConNombre("Gerente B").ConEmail($"gerente-b-{Guid.NewGuid():N}@test.com").CrearAsync(db);

        AuthenticateAs(999, 0, "SuperAdmin");
        var response = await Client.GetAsync($"/api/admin/comercios/{comercioA.Id}/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(duenoA.Id, ids);
        Assert.Contains(cajeroA.Id, ids);
        Assert.DoesNotContain(usuarioB.Id, ids);

        // Campos que espera ComercioDetallePage.tsx: id, nombre, email, rol, activo
        var cajero = body.EnumerateArray().First(e => e.GetProperty("id").GetInt32() == cajeroA.Id);
        Assert.Equal("Cajero A", cajero.GetProperty("nombre").GetString());
        Assert.Equal(cajeroA.Email, cajero.GetProperty("email").GetString());
        Assert.Equal("Cajero", cajero.GetProperty("rol").GetString());
        Assert.False(cajero.GetProperty("activo").GetBoolean());
    }

    [DockerAvailableFact]
    public async Task Respuesta_NoExponeHashDeContrasenaNiDatosSensibles()
    {
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var usuario = await TestDataBuilder.Usuario().EnComercio(comercio.Id).EnSucursal(sucursal.Id)
            .ConRol("Dueño").CrearAsync(db);
        Assert.False(string.IsNullOrEmpty(usuario.PasswordHash));

        AuthenticateAs(999, 0, "SuperAdmin");
        var response = await Client.GetAsync($"/api/admin/comercios/{comercio.Id}/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(usuario.PasswordHash, raw);
        Assert.DoesNotContain("passwordhash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debeCambiarPassword", raw, StringComparison.OrdinalIgnoreCase);

        var body = JsonSerializer.Deserialize<JsonElement>(raw);
        var campos = body[0].EnumerateObject().Select(p => p.Name.ToLowerInvariant()).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "activo", "email", "id", "nombre", "rol" }, campos);
    }

    [DockerAvailableFact]
    public async Task ComercioInexistente_Devuelve404ConCodigoComercioNotFound()
    {
        AuthenticateAs(999, 0, "SuperAdmin");
        var response = await Client.GetAsync("/api/admin/comercios/987654/usuarios");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("COMERCIO_NOT_FOUND", body.GetProperty("code").GetString());
    }

    [DockerAvailableFact]
    public Task Dueno_Recibe403() => VerificarRol403Async("Dueño");

    [DockerAvailableFact]
    public Task Gerente_Recibe403() => VerificarRol403Async("Gerente");

    [DockerAvailableFact]
    public Task Cajero_Recibe403() => VerificarRol403Async("Cajero");

    private async Task VerificarRol403Async(string rol)
    {
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var usuario = await TestDataBuilder.Usuario().EnComercio(comercio.Id).EnSucursal(sucursal.Id)
            .ConRol(rol).CrearAsync(db);

        AuthenticateAs(usuario.Id, comercio.Id, rol);
        var response = await Client.GetAsync($"/api/admin/comercios/{comercio.Id}/usuarios");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
