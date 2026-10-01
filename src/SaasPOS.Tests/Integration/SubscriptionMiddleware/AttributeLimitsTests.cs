using System.Net;
using System.Net.Http.Json;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.SubscriptionMiddleware;

/// <summary>
/// Tests that the subscription middleware enforces attribute limits per category per plan.
/// Plan Básico: max 2 attributes per category.
/// Plan Intermedio: max 5 attributes per category.
/// Plan Empresarial: unlimited attributes.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "SubscriptionMiddleware")]
public class AttributeLimitsTests : IntegrationTestBase
{
    public AttributeLimitsTests(PostgresFixture fixture) : base(fixture) { }

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
    }

    [DockerAvailableFact]
    public async Task PlanBasico_RejectsThirdAttribute_Returns403()
    {
        // Arrange: Create comercio with Plan Básico (limit = 2 attributes per category)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(1).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var categoria = await TestDataBuilder.Categoria().EnComercio(comercio.Id).CrearAsync(db);

        // Seed 2 attributes directly (at limit)
        for (int i = 1; i <= 2; i++)
        {
            db.AtributosCategoria.Add(new AtributoCategoria
            {
                CategoriaId = categoria.Id,
                NombreAtributo = $"Atributo {i}",
                TipoDato = "Texto"
            });
        }
        await db.SaveChangesAsync();

        // Create a Gerente user for authentication
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente (required by CanManageProducts policy)
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new { NombreAtributo = "Atributo 3", TipoDato = "Texto" };

        // Act: Try to create a third attribute (exceeds limit of 2)
        var response = await Client.PostAsJsonAsync($"/api/tenants/categorias/{categoria.Id}/atributos", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task PlanIntermedio_RejectsSixthAttribute_Returns403()
    {
        // Arrange: Create comercio with Plan Intermedio (limit = 5 attributes per category)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(2).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var categoria = await TestDataBuilder.Categoria().EnComercio(comercio.Id).CrearAsync(db);

        // Seed 5 attributes directly (at limit)
        for (int i = 1; i <= 5; i++)
        {
            db.AtributosCategoria.Add(new AtributoCategoria
            {
                CategoriaId = categoria.Id,
                NombreAtributo = $"Atributo {i}",
                TipoDato = "Texto"
            });
        }
        await db.SaveChangesAsync();

        // Create a Gerente user for authentication
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new { NombreAtributo = "Atributo 6", TipoDato = "Texto" };

        // Act: Try to create a sixth attribute (exceeds limit of 5)
        var response = await Client.PostAsJsonAsync($"/api/tenants/categorias/{categoria.Id}/atributos", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task PlanEmpresarial_AllowsUnlimitedAttributes_Returns201()
    {
        // Arrange: Create comercio with Plan Empresarial (unlimited attributes)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var categoria = await TestDataBuilder.Categoria().EnComercio(comercio.Id).CrearAsync(db);

        // Seed 10 attributes directly (well beyond other plans' limits)
        for (int i = 1; i <= 10; i++)
        {
            db.AtributosCategoria.Add(new AtributoCategoria
            {
                CategoriaId = categoria.Id,
                NombreAtributo = $"Atributo {i}",
                TipoDato = "Texto"
            });
        }
        await db.SaveChangesAsync();

        // Create a Gerente user for authentication
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        var request = new { NombreAtributo = "Atributo 11", TipoDato = "Texto" };

        // Act: Create an eleventh attribute (should succeed — no limit)
        var response = await Client.PostAsJsonAsync($"/api/tenants/categorias/{categoria.Id}/atributos", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
