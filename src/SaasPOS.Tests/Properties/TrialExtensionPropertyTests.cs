using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

// Feature: prueba-gratuita, Property 13: Extensión de trial suma días correctamente

/// <summary>
/// Property-based tests para la extensión de trial.
/// Verifica que para cualquier Suscripción Trial y n en [1,15],
/// la nueva FechaProximoCorte = anterior + n días.
/// **Validates: Requirements 6.2**
/// </summary>
public class TrialExtensionPropertyTests
{
    private static readonly Mock<ILogger<TrialService>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto de base de datos en memoria con el plan Básico pre-cargado.
    /// </summary>
    private static (AppDbContext db, TrialService service) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        // Sembrar el Plan Básico
        db.Planes.Add(
            new Plan { Id = 1, Nombre = "Básico", Precio = 350m, LimiteUsuarios = 2, LimiteAtributos = 2, LimiteSucursales = 1 }
        );
        db.SaveChanges();

        var service = new TrialService(db, LoggerMock.Object);
        return (db, service);
    }

    /// <summary>
    /// Crea un Comercio y Suscripción en estado Trial con una FechaProximoCorte conocida.
    /// </summary>
    private static Suscripcion SeedComercioConTrial(AppDbContext db)
    {
        var comercio = new Comercio
        {
            Id = 100,
            Ruc = "0990000000100",
            RazonSocial = "Comercio Test Extension",
            PlanId = 1,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow.AddDays(-10)
        };

        db.Comercios.Add(comercio);
        db.SaveChanges();

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = 1,
            FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            MontoCuota = 0,
            EsProporcional = false,
            Estado = "Trial",
            DiasExtendidos = 0
        };

        db.Suscripciones.Add(suscripcion);
        db.SaveChanges();

        return suscripcion;
    }

    // ─── Propiedad 13: Extensión de trial suma días correctamente ─────────────

    /// <summary>
    /// Para cualquier Suscripción Trial y n en [1,15],
    /// la nueva FechaProximoCorte = anterior + n días y DiasExtendidos se actualiza.
    /// **Validates: Requirements 6.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Extension_Trial_Suma_Dias_Correctamente()
    {
        return Prop.ForAll(
            Gen.Choose(1, 15).ToArbitrary(),
            (int diasAdicionales) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    var suscripcion = SeedComercioConTrial(db);

                    // Guardar la fecha anterior antes de la extensión
                    var fechaAnterior = suscripcion.FechaProximoCorte;
                    var diasExtendidosAnterior = suscripcion.DiasExtendidos;

                    var request = new ExtendTrialRequest(diasAdicionales);

                    // Ejecutar extensión
                    var resultado = service.ExtenderTrialAsync(suscripcion.Id, request, superAdminId: 1)
                        .GetAwaiter().GetResult();

                    // Recargar entidad desde DB
                    var suscripcionActualizada = db.Suscripciones.First(s => s.Id == suscripcion.Id);

                    // Verificar que la nueva FechaProximoCorte == anterior + DiasAdicionales
                    var fechaEsperada = fechaAnterior.AddDays(diasAdicionales);
                    var fechaCorrecta = suscripcionActualizada.FechaProximoCorte == fechaEsperada;

                    // Verificar que DiasExtendidos se actualizó correctamente
                    var diasExtendidosCorrecto = suscripcionActualizada.DiasExtendidos == diasExtendidosAnterior + diasAdicionales;

                    // Verificar que el DTO retorna la fecha correcta
                    var dtoFechaCorrecta = resultado.NuevaFechaProximoCorte == fechaEsperada;

                    return fechaCorrecta && diasExtendidosCorrecto && dtoFechaCorrecta;
                }
            });
    }
}
