using System.Net;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.SubscriptionMiddleware;

/// <summary>
/// Tests that the subscription middleware enforces report access per plan.
/// Plan Básico: no reports allowed (403).
/// Plan Intermedio: predefined reports only (top-producto, top-categoria, top-sucursal).
/// Plan Empresarial: all reports including personalizado.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "SubscriptionMiddleware")]
public class ReportAccessTests : IntegrationTestBase
{
    public ReportAccessTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task PlanBasico_ReportsEndpoint_Returns403()
    {
        // Arrange: Create comercio with Plan Básico (no reports allowed)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(1).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create a Gerente user for authentication
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente (required by CanViewReports policy)
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Act: Try to access a predefined report
        var response = await Client.GetAsync("/api/tenants/reportes/top-producto");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task PlanIntermedio_PredefinedReports_Returns200()
    {
        // Arrange: Create comercio with Plan Intermedio (predefined reports allowed)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(2).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create a Gerente user for authentication
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Act: Access a predefined report
        var response = await Client.GetAsync("/api/tenants/reportes/top-producto");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task PlanIntermedio_CustomReport_Returns403()
    {
        // Arrange: Create comercio with Plan Intermedio (custom reports NOT allowed)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(2).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create a Gerente user for authentication
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Act: Try to access custom report (only Empresarial can)
        var response = await Client.GetAsync("/api/tenants/reportes/personalizado");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task PlanEmpresarial_CustomReport_Returns200()
    {
        // Arrange: Create comercio with Plan Empresarial (all reports allowed)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create a Gerente user for authentication
        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Authenticate as Gerente
        AuthenticateAs(gerente.Id, comercio.Id, "Gerente");

        // Act: Access custom report
        var response = await Client.GetAsync("/api/tenants/reportes/personalizado");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
