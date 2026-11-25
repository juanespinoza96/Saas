using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Regresión de codificación: el nombre de plan "Básico" (UTF-8 correcto) debe reconocerse
/// en <see cref="ReportEngine"/>. Antes la constante estaba doblemente codificada ("BÃ¡sico"),
/// no coincidía con el plan real y el motor respondía con el mensaje genérico de plan no determinado.
/// </summary>
public class ReportEnginePlanBasicoTests
{
    private static ReportEngine CreateEngine()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options, tenantContextMock.Object);

        return new ReportEngine(db, new Mock<ILogger<ReportEngine>>().Object);
    }

    [Fact]
    public async Task GenerarReporteAsync_PlanBasico_LanzaForbiddenConMensajeDePlanBasico()
    {
        var engine = CreateEngine();
        var request = new ReportRequest(TipoReporte.TopProducto);

        var ex = await Assert.ThrowsAsync<ReportForbiddenException>(
            () => engine.GenerarReporteAsync(request, comercioId: 1, planNivel: "Básico"));

        Assert.Contains("El Plan Básico no incluye acceso a reportes", ex.Message);
        Assert.DoesNotContain("No se pudo determinar el nivel de plan", ex.Message);
    }

    [Fact]
    public async Task GenerarReporteAsync_PlanConCodificacionDanada_NoSeReconoceComoBasico()
    {
        // Fija el contrato: solo el texto UTF-8 correcto coincide; el mojibake cae en el rechazo genérico.
        var engine = CreateEngine();
        var request = new ReportRequest(TipoReporte.TopProducto);

        var ex = await Assert.ThrowsAsync<ReportForbiddenException>(
            () => engine.GenerarReporteAsync(request, comercioId: 1, planNivel: "B\u00C3\u00A1sico"));

        Assert.Contains("No se pudo determinar el nivel de plan", ex.Message);
    }
}
