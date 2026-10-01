using System.Net;
using System.Text;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.SubscriptionMiddleware;

/// <summary>
/// Tests that the subscription middleware blocks SRI electronic invoicing for Plan Básico.
/// Plan Básico with UsaFacturacionSRI=true: emitir-factura returns 403 (FACTURACION_BLOQUEADA).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "SubscriptionMiddleware")]
public class SriAccessTests : IntegrationTestBase
{
    public SriAccessTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task PlanBasico_WithSRI_EmitirFactura_Returns403()
    {
        // Arrange: Create comercio with Plan Básico and UsaFacturacionSRI = true
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio()
            .ConPlan(1)
            .ConFacturacionSRI(true)
            .CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Create a Cajero user for authentication (required by CanSell policy)
        var cajero = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Cajero").CrearAsync(db);

        // Create a Venta record that the emitir-factura endpoint requires
        var venta = new Venta
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            UsuarioId = cajero.Id,
            TipoComprobante = "Factura Electronica",
            MetodoPago = "Efectivo",
            Total = 100m,
            FechaVenta = DateTime.UtcNow,
            EstadoSRI = "Pendiente"
        };
        db.Ventas.Add(venta);
        await db.SaveChangesAsync();

        // Authenticate as Cajero
        AuthenticateAs(cajero.Id, comercio.Id, "Cajero");

        // Act: Try to emit electronic invoice (Plan Básico is blocked)
        var response = await Client.PostAsync(
            $"/api/tenants/ventas/{venta.Id}/emitir-factura",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
