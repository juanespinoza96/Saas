using System.Net;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Tests de integración para el endpoint GET /api/tenants/reportes/exportar
/// con personalización de componentes PDF según plan.
/// Valida: Req 3.1, 3.4, 3.5, 5.1, 6.1, 6.2, 6.3
/// </summary>
[Collection("Postgres")]
public class ReportExportIntegrationTests : IntegrationTestBase
{
    public ReportExportIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
        await SeedPlansAsync();
    }

    // ── Helpers de seeding ──────────────────────────────────────────────────────

    /// <summary>
    /// Crea los 3 planes base con Ids fijos (1=Básico, 2=Intermedio, 3=Empresarial).
    /// </summary>
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

    /// <summary>
    /// Crea un comercio completo con datos mínimos para que el motor de reportes
    /// pueda generar un resultado exportable (al menos una Venta con DetalleVenta).
    /// Retorna (comercioId, sucursalId, usuarioId).
    /// </summary>
    private async Task<(int comercioId, int sucursalId, int usuarioId)> SeedComercioConVentas(int planId)
    {
        await using var db = CreateDbContext();

        // Comercio con zona horaria configurada
        var comercio = new Comercio
        {
            Ruc = $"17{Guid.NewGuid().ToString("N")[..11]}",
            RazonSocial = "Comercio Export Test",
            PlanId = planId,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow,
            ZonaHorariaDefecto = "America/Guayaquil"
        };
        db.Comercios.Add(comercio);
        await db.SaveChangesAsync();

        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        var gerente = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id)
            .EnSucursal(sucursal.Id)
            .ConRol("Gerente")
            .CrearAsync(db);

        // Categoría y Producto necesarios para que el reporte tenga datos
        var categoria = await TestDataBuilder.Categoria().EnComercio(comercio.Id).CrearAsync(db);

        var producto = await TestDataBuilder.Producto()
            .EnComercio(comercio.Id)
            .EnCategoria(categoria.Id)
            .ConPrecio(15.00m)
            .CrearAsync(db);

        // Venta en el mes actual (el motor de reportes filtra por mes actual)
        var venta = new Venta
        {
            ComercioId = comercio.Id,
            SucursalId = sucursal.Id,
            UsuarioId = gerente.Id,
            Total = 45.00m,
            TipoComprobante = "Ticket Interno",
            MetodoPago = "Efectivo",
            FechaVenta = DateTime.UtcNow
        };
        db.Set<Venta>().Add(venta);
        await db.SaveChangesAsync();

        // Detalle de venta asociado
        var detalle = new DetalleVenta
        {
            VentaId = venta.Id,
            ProductoId = producto.Id,
            Cantidad = 3,
            PrecioRealCobrado = 15.00m
        };
        db.Set<DetalleVenta>().Add(detalle);
        await db.SaveChangesAsync();

        return (comercio.Id, sucursal.Id, gerente.Id);
    }

    // ── Tests ────────────────────────────────────────────────────────────────────

    [DockerAvailableFact]
    public async Task ExportarPdf_PlanEmpresarial_ConOpciones_Returns200Pdf()
    {
        // Arrange: Comercio con Plan Empresarial y datos de ventas
        var (comercioId, _, usuarioId) = await SeedComercioConVentas(planId: 3);
        AuthenticateAs(usuarioId, comercioId, "Gerente");

        // Act: Exportar PDF con personalización explícita (solo tabla y barras)
        var response = await Client.GetAsync(
            "/api/tenants/reportes/exportar?tipo=TopProducto&formato=Pdf&incluirGraficoBarras=true&incluirGraficoPastel=false&incluirTabla=true");

        // Assert: Respuesta exitosa con PDF válido
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0, "El PDF generado no debe estar vacío");
    }

    [DockerAvailableFact]
    public async Task ExportarPdf_PlanIntermedio_IgnoraParams_Returns200Pdf()
    {
        // Arrange: Comercio con Plan Intermedio
        var (comercioId, _, usuarioId) = await SeedComercioConVentas(planId: 2);
        AuthenticateAs(usuarioId, comercioId, "Gerente");

        // Act: Enviar params de personalización que deben ser ignorados
        var response = await Client.GetAsync(
            "/api/tenants/reportes/exportar?tipo=TopProducto&formato=Pdf&incluirGraficoBarras=false&incluirGraficoPastel=true&incluirTabla=false");

        // Assert: Respuesta exitosa — el Plan Intermedio ignora params y genera PDF con estructura fija
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0, "El PDF generado no debe estar vacío");
    }

    [DockerAvailableFact]
    public async Task ExportarPdf_PlanBasico_Returns403()
    {
        // Arrange: Comercio con Plan Básico (sin acceso a reportes)
        var (comercioId, _, usuarioId) = await SeedComercioConVentas(planId: 1);
        AuthenticateAs(usuarioId, comercioId, "Gerente");

        // Act: Intentar exportar PDF
        var response = await Client.GetAsync(
            "/api/tenants/reportes/exportar?tipo=TopProducto&formato=Pdf");

        // Assert: Acceso denegado por plan
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerAvailableFact]
    public async Task ExportarCsv_ConParamsPersonalizacion_Returns200CsvSinAfectar()
    {
        // Arrange: Comercio con Plan Empresarial
        var (comercioId, _, usuarioId) = await SeedComercioConVentas(planId: 3);
        AuthenticateAs(usuarioId, comercioId, "Gerente");

        // Act: Exportar CSV con params de personalización (deben ser ignorados)
        var response = await Client.GetAsync(
            "/api/tenants/reportes/exportar?tipo=TopProducto&formato=Csv&incluirGraficoBarras=false&incluirGraficoPastel=false&incluirTabla=false");

        // Assert: CSV generado correctamente (los params visuales no afectan al CSV)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0, "El CSV generado no debe estar vacío");
    }

    [DockerAvailableFact]
    public async Task ExportarPdf_PlanEmpresarial_TodosComponentesFalse_Returns400()
    {
        // Arrange: Comercio con Plan Empresarial
        var (comercioId, _, usuarioId) = await SeedComercioConVentas(planId: 3);
        AuthenticateAs(usuarioId, comercioId, "Gerente");

        // Act: Enviar todos los componentes como false (inválido)
        var response = await Client.GetAsync(
            "/api/tenants/reportes/exportar?tipo=TopProducto&formato=Pdf&incluirGraficoBarras=false&incluirGraficoPastel=false&incluirTabla=false");

        // Assert: Error 400 — debe seleccionar al menos un componente
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
