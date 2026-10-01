using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Audit;

/// <summary>
/// Tests that critical operations generate correct audit log entries in LogsAuditoria.
/// Covers user creation audit (15.1), field correctness (15.2), and ON DELETE SET NULL (15.4).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Audit")]
public class AuditLogCreationTests : IntegrationTestBase
{
    public AuditLogCreationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
        await SeedPlansAsync();
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

    // ── 15.1 & 15.2: User Create Generates Audit Log ──────────────────────────

    [DockerAvailableFact]
    public async Task UserCreate_GeneratesAuditLog_WithCorrectFields()
    {
        // Arrange: Create comercio (Empresarial), sucursal, gerente
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new
        {
            Nombre = "Nuevo Cajero",
            Email = $"cajero-{Guid.NewGuid():N}@test.com",
            Password = "Password123!",
            Rol = "Cajero",
            SucursalId = sucursal.Id
        };

        // Act: Create a new user via API
        var response = await Client.PostAsJsonAsync("/api/tenants/usuarios", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Extract the created user's ID from response
        var createdUser = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var createdUserId = createdUser.GetProperty("id").GetInt32();

        // Assert: Query DB for the audit log
        await using var verifyDb = CreateDbContext();
        var auditLog = await verifyDb.LogsAuditoria
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l =>
                l.ComercioId == comercio.Id &&
                l.Accion == "Crear" &&
                l.TablaAfectada == "Usuarios");

        Assert.NotNull(auditLog);
        Assert.Equal(comercio.Id, auditLog.ComercioId);
        Assert.Equal(gerente.Id, auditLog.UsuarioId);
        Assert.Equal(createdUserId.ToString(), auditLog.RegistroId);
        Assert.NotNull(auditLog.ValoresNuevos);
        Assert.Null(auditLog.ValoresAnteriores); // Create has no previous values
    }

    // ── 15.2: Product Update Generates Audit Log With Previous and New Values ─

    [DockerAvailableFact]
    public async Task ProductUpdate_GeneratesAuditLog_WithPreviousAndNewValues()
    {
        // Arrange: Create comercio, sucursal, gerente, and a product
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Create a product first
        var createRequest = new CreateProductoRequest(
            Nombre: "Producto Original",
            TipoArticulo: "Venta Directa",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Unidades",
            CostoProduccion: 2m,
            PrecioLista: 5m,
            PrecioMinimo: 3m);

        var createResponse = await Client.PostAsJsonAsync("/api/tenants/productos", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdDto = await createResponse.Content.ReadFromJsonAsync<ProductoDto>();

        // Act: Update the product
        var updateRequest = new UpdateProductoRequest(
            Nombre: "Producto Modificado",
            TipoArticulo: "Venta Directa",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Unidades",
            CostoProduccion: 3m,
            PrecioLista: 8m,
            PrecioMinimo: 5m);

        var updateResponse = await Client.PutAsJsonAsync($"/api/tenants/productos/{createdDto!.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Assert: Query DB for the update audit log
        await using var verifyDb = CreateDbContext();
        var auditLog = await verifyDb.LogsAuditoria
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l =>
                l.ComercioId == comercio.Id &&
                l.Accion == "Actualizar" &&
                l.TablaAfectada == "Productos" &&
                l.RegistroId == createdDto.Id.ToString());

        Assert.NotNull(auditLog);
        Assert.Equal(gerente.Id, auditLog.UsuarioId);
        Assert.Equal(createdDto.Id.ToString(), auditLog.RegistroId);
        Assert.NotNull(auditLog.ValoresAnteriores);
        Assert.NotNull(auditLog.ValoresNuevos);

        // Verify JSON content contains relevant field data
        Assert.Contains("Producto", auditLog.ValoresAnteriores);
        Assert.Contains("Producto Modificado", auditLog.ValoresNuevos);
    }

    // ── 15.4: ON DELETE SET NULL preserves audit log record ────────────────────

    [DockerAvailableFact]
    public async Task UserDeletion_PreservesAuditLog_WithNullUsuarioId()
    {
        // Arrange: Create comercio, sucursal, gerente
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Have the gerente create a product (generates audit log with UsuarioId=gerente.Id)
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var productRequest = new CreateProductoRequest(
            Nombre: "Producto Para Auditar",
            TipoArticulo: "Venta Directa",
            CategoriaId: null,
            ValoresDinamicos: null,
            ManejaStock: true,
            UnidadMedida: "Unidades",
            CostoProduccion: 1m,
            PrecioLista: 4m,
            PrecioMinimo: 2m);

        var response = await Client.PostAsJsonAsync("/api/tenants/productos", productRequest);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Verify the audit log exists with UsuarioId = gerente.Id
        await using var verifyDb = CreateDbContext();
        var auditBefore = await verifyDb.LogsAuditoria
            .IgnoreQueryFilters()
            .FirstAsync(l => l.UsuarioId == gerente.Id && l.TablaAfectada == "Productos");
        Assert.Equal(gerente.Id, auditBefore.UsuarioId);

        // Act: Delete the gerente from DB directly (simulating user hard-deletion)
        await using var deleteDb = CreateDbContext();
        var gerenteEntity = await deleteDb.Usuarios.IgnoreQueryFilters().FirstAsync(u => u.Id == gerente.Id);
        deleteDb.Usuarios.Remove(gerenteEntity);
        await deleteDb.SaveChangesAsync();

        // Assert: The audit log still exists, but UsuarioId is now NULL (ON DELETE SET NULL)
        await using var afterDb = CreateDbContext();
        var auditAfter = await afterDb.LogsAuditoria
            .IgnoreQueryFilters()
            .FirstAsync(l => l.Id == auditBefore.Id);

        Assert.Null(auditAfter.UsuarioId);
        Assert.Equal("Productos", auditAfter.TablaAfectada);
        Assert.Equal("Crear", auditAfter.Accion);
        Assert.NotNull(auditAfter.ValoresNuevos); // Log content is preserved
    }
}
