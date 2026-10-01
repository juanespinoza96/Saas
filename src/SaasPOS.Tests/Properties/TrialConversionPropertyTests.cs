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

// Feature: prueba-gratuita, Property 11: Conversión de trial produce estado correcto

/// <summary>
/// Property-based tests para la conversión de trial a suscripción paga.
/// Verifica que para cualquier Suscripción Trial/Trial_Expirado con Plan válido,
/// la conversión produce el estado correcto en todas las propiedades.
/// **Validates: Requirements 5.2, 5.3, 5.4**
/// </summary>
public class TrialConversionPropertyTests
{
    private static readonly Mock<ILogger<TrialService>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto de base de datos en memoria con planes pre-cargados.
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

        // Sembrar los planes disponibles
        db.Planes.AddRange(
            new Plan { Id = 1, Nombre = "Básico", Precio = 350m, LimiteUsuarios = 2, LimiteAtributos = 2, LimiteSucursales = 1 },
            new Plan { Id = 2, Nombre = "Intermedio", Precio = 750m, LimiteUsuarios = 5, LimiteAtributos = 5, LimiteSucursales = 3 },
            new Plan { Id = 3, Nombre = "Empresarial", Precio = 1200m, LimiteUsuarios = 0, LimiteAtributos = 0, LimiteSucursales = 0 }
        );
        db.SaveChanges();

        var service = new TrialService(db, LoggerMock.Object);
        return (db, service);
    }

    /// <summary>
    /// Crea un Comercio y Suscripción en el estado indicado para pruebas de conversión.
    /// </summary>
    private static (Comercio comercio, Suscripcion suscripcion) SeedComercioConTrial(
        AppDbContext db, string estadoSuscripcion, int comercioId = 100)
    {
        var estadoComercio = estadoSuscripcion == "Trial_Expirado" ? "Suspendido" : "Activo";

        var comercio = new Comercio
        {
            Id = comercioId,
            Ruc = $"0990{comercioId:D9}",
            RazonSocial = $"Comercio Test {comercioId}",
            PlanId = 1, // Plan Básico durante trial
            Estado = estadoComercio,
            FechaRegistro = DateTime.UtcNow.AddDays(-10)
        };

        db.Comercios.Add(comercio);
        db.SaveChanges();

        var suscripcion = new Suscripcion
        {
            ComercioId = comercio.Id,
            PlanId = 1, // Plan Básico durante trial
            FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            MontoCuota = 0,
            EsProporcional = false,
            Estado = estadoSuscripcion,
            DiasExtendidos = 0
        };

        db.Suscripciones.Add(suscripcion);
        db.SaveChanges();

        return (comercio, suscripcion);
    }

    /// <summary>
    /// Mapea un valor de PlanId generado (1, 2 o 3) al precio esperado.
    /// </summary>
    private static decimal ObtenerPrecioEsperado(int planId) => planId switch
    {
        1 => 350m,
        2 => 750m,
        3 => 1200m,
        _ => 0m
    };

    /// <summary>
    /// Mapea un valor de PlanId generado (1, 2 o 3) al nombre esperado.
    /// </summary>
    private static string ObtenerNombreEsperado(int planId) => planId switch
    {
        1 => "Básico",
        2 => "Intermedio",
        3 => "Empresarial",
        _ => ""
    };

    // ─── Propiedad 11a: Conversión de Trial a Activa ──────────────────────────

    /// <summary>
    /// Para cualquier Suscripcion con Estado "Trial" y cualquier Plan válido,
    /// la conversión produce: Estado="Activa", PlanId correcto, MontoCuota=plan.Precio,
    /// FechaProximoCorte≈hoy+30, FechaInicio≈hoy.
    /// **Validates: Requirements 5.2, 5.3, 5.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Conversion_Trial_A_Activa_Produce_Estado_Correcto(PositiveInt planIdGen)
    {
        // Seleccionar un PlanId válido entre 1, 2 y 3
        var planId = (planIdGen.Get % 3) + 1;

        var (db, service) = CreateContext();
        using (db)
        {
            var (comercio, suscripcion) = SeedComercioConTrial(db, "Trial");

            var request = new ConvertTrialRequest(planId);
            var fechaAntesDeConversion = DateOnly.FromDateTime(DateTime.UtcNow);

            // Ejecutar conversión
            var resultado = service.ConvertirTrialAsync(suscripcion.Id, request, superAdminId: 1)
                .GetAwaiter().GetResult();

            // Recargar entidad desde DB
            var suscripcionActualizada = db.Suscripciones
                .Include(s => s.Comercio)
                .First(s => s.Id == suscripcion.Id);

            var precioEsperado = ObtenerPrecioEsperado(planId);
            var nombreEsperado = ObtenerNombreEsperado(planId);

            // Verificar Estado = "Activa"
            var estadoCorrecto = suscripcionActualizada.Estado == "Activa";

            // Verificar PlanId correcto
            var planCorrecto = suscripcionActualizada.PlanId == planId;

            // Verificar MontoCuota = plan.Precio
            var montoCorrecto = suscripcionActualizada.MontoCuota == precioEsperado;

            // Verificar FechaProximoCorte ≈ hoy + 30 días (tolerancia de 1 día)
            var fechaCorteEsperada = fechaAntesDeConversion.AddDays(30);
            var diferenciaCorte = Math.Abs(suscripcionActualizada.FechaProximoCorte.DayNumber - fechaCorteEsperada.DayNumber);
            var fechaCorteCercana = diferenciaCorte <= 1;

            // Verificar FechaInicio ≈ hoy (tolerancia de 1 día)
            var diferenciaInicio = Math.Abs(suscripcionActualizada.FechaInicio.DayNumber - fechaAntesDeConversion.DayNumber);
            var fechaInicioCercana = diferenciaInicio <= 1;

            // Verificar DTO de retorno
            var dtoNombreCorrecto = resultado.NombrePlan == nombreEsperado;
            var dtoMontoCorrecto = resultado.MontoCuota == precioEsperado;

            return estadoCorrecto && planCorrecto && montoCorrecto &&
                   fechaCorteCercana && fechaInicioCercana &&
                   dtoNombreCorrecto && dtoMontoCorrecto;
        }
    }

    // ─── Propiedad 11b: Conversión de Trial_Expirado reactiva comercio ────────

    /// <summary>
    /// Para cualquier Suscripcion con Estado "Trial_Expirado" (Comercio suspendido)
    /// y cualquier Plan válido, la conversión además de cambiar la suscripción a Activa,
    /// reactiva el Comercio cambiando su Estado a "Activo".
    /// **Validates: Requirements 5.2, 5.3, 5.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Conversion_TrialExpirado_Reactiva_Comercio(PositiveInt planIdGen)
    {
        // Seleccionar un PlanId válido entre 1, 2 y 3
        var planId = (planIdGen.Get % 3) + 1;

        var (db, service) = CreateContext();
        using (db)
        {
            var (comercio, suscripcion) = SeedComercioConTrial(db, "Trial_Expirado");

            // Confirmar precondición: comercio suspendido antes de la conversión
            if (comercio.Estado != "Suspendido")
                return false;

            var request = new ConvertTrialRequest(planId);
            var fechaAntesDeConversion = DateOnly.FromDateTime(DateTime.UtcNow);

            // Ejecutar conversión
            var resultado = service.ConvertirTrialAsync(suscripcion.Id, request, superAdminId: 1)
                .GetAwaiter().GetResult();

            // Recargar entidades desde DB
            var suscripcionActualizada = db.Suscripciones
                .Include(s => s.Comercio)
                .First(s => s.Id == suscripcion.Id);

            var precioEsperado = ObtenerPrecioEsperado(planId);

            // Verificar que el Comercio fue reactivado a "Activo"
            var comercioReactivado = suscripcionActualizada.Comercio.Estado == "Activo";

            // Verificar Estado suscripción = "Activa"
            var estadoCorrecto = suscripcionActualizada.Estado == "Activa";

            // Verificar PlanId correcto
            var planCorrecto = suscripcionActualizada.PlanId == planId;

            // Verificar MontoCuota = plan.Precio
            var montoCorrecto = suscripcionActualizada.MontoCuota == precioEsperado;

            // Verificar FechaProximoCorte ≈ hoy + 30 días (tolerancia de 1 día)
            var fechaCorteEsperada = fechaAntesDeConversion.AddDays(30);
            var diferenciaCorte = Math.Abs(suscripcionActualizada.FechaProximoCorte.DayNumber - fechaCorteEsperada.DayNumber);
            var fechaCorteCercana = diferenciaCorte <= 1;

            // Verificar FechaInicio ≈ hoy (tolerancia de 1 día)
            var diferenciaInicio = Math.Abs(suscripcionActualizada.FechaInicio.DayNumber - fechaAntesDeConversion.DayNumber);
            var fechaInicioCercana = diferenciaInicio <= 1;

            return comercioReactivado && estadoCorrecto && planCorrecto &&
                   montoCorrecto && fechaCorteCercana && fechaInicioCercana;
        }
    }

    // ─── Propiedad combinada: cualquier estado válido con cualquier plan ─────

    /// <summary>
    /// Para cualquier combinación de estado inicial (Trial/Trial_Expirado, generado
    /// mediante un booleano aleatorio) y plan válido, la conversión siempre produce
    /// los campos correctos, incluyendo reactivación del comercio si era Trial_Expirado.
    /// **Validates: Requirements 5.2, 5.3, 5.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Conversion_Cualquier_Estado_Valido_Produce_Resultado_Correcto(
        PositiveInt planIdGen, bool usarTrialExpirado)
    {
        // Seleccionar un PlanId válido entre 1, 2 y 3
        var planId = (planIdGen.Get % 3) + 1;
        var estadoInicial = usarTrialExpirado ? "Trial_Expirado" : "Trial";

        var (db, service) = CreateContext();
        using (db)
        {
            var (comercio, suscripcion) = SeedComercioConTrial(db, estadoInicial);

            var request = new ConvertTrialRequest(planId);
            var fechaAntesDeConversion = DateOnly.FromDateTime(DateTime.UtcNow);

            // Ejecutar conversión
            var resultado = service.ConvertirTrialAsync(suscripcion.Id, request, superAdminId: 1)
                .GetAwaiter().GetResult();

            // Recargar entidades desde DB
            var suscripcionActualizada = db.Suscripciones
                .Include(s => s.Comercio)
                .First(s => s.Id == suscripcion.Id);

            var precioEsperado = ObtenerPrecioEsperado(planId);

            // Estado de suscripción siempre debe ser "Activa"
            var estadoCorrecto = suscripcionActualizada.Estado == "Activa";

            // PlanId debe corresponder al plan seleccionado
            var planCorrecto = suscripcionActualizada.PlanId == planId;

            // MontoCuota debe ser el precio del plan
            var montoCorrecto = suscripcionActualizada.MontoCuota == precioEsperado;

            // FechaProximoCorte ≈ hoy + 30 (tolerancia 1 día)
            var fechaCorteEsperada = fechaAntesDeConversion.AddDays(30);
            var diferenciaCorte = Math.Abs(suscripcionActualizada.FechaProximoCorte.DayNumber - fechaCorteEsperada.DayNumber);
            var fechaCorteCercana = diferenciaCorte <= 1;

            // FechaInicio ≈ hoy (tolerancia 1 día)
            var diferenciaInicio = Math.Abs(suscripcionActualizada.FechaInicio.DayNumber - fechaAntesDeConversion.DayNumber);
            var fechaInicioCercana = diferenciaInicio <= 1;

            // Si era Trial_Expirado, el Comercio debe ser reactivado a "Activo"
            var comercioEstadoCorrecto = usarTrialExpirado
                ? suscripcionActualizada.Comercio.Estado == "Activo"
                : true; // Si era Trial, el comercio ya estaba activo

            return estadoCorrecto && planCorrecto && montoCorrecto &&
                   fechaCorteCercana && fechaInicioCercana && comercioEstadoCorrecto;
        }
    }
}
