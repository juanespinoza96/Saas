using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

// Feature: prueba-gratuita, Property 16: Ordenamiento del listado de trials

/// <summary>
/// Property-based tests para el ordenamiento del listado de trials.
/// Verificar que el listado está siempre ordenado por días restantes ascendente.
/// **Validates: Requirements 7.6**
/// </summary>
public class TrialOrdenamientoPropertyTests
{
    private static readonly Mock<ILogger<TrialService>> LoggerMock = new();

    /// <summary>
    /// Crea un contexto de base de datos InMemory con las advertencias de transacciones suprimidas
    /// y un TrialService listo para usar.
    /// </summary>
    private static (AppDbContext db, TrialService service) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        // Sembrar el Plan Básico
        db.Planes.Add(new Plan
        {
            Id = 1,
            Nombre = "Básico",
            Precio = 350m,
            LimiteUsuarios = 2,
            LimiteAtributos = 2,
            LimiteSucursales = 1
        });
        db.SaveChanges();

        var service = new TrialService(db, LoggerMock.Object);
        return (db, service);
    }

    // ─── Property 16: Ordenamiento del listado de trials ─────────────────────

    /// <summary>
    /// Para cualquier conjunto de suscripciones Trial/Trial_Expirado con fechas de corte variadas,
    /// ObtenerTrialsAsync() retorna la lista ordenada por DiasRestantes ascendente.
    /// Cada elemento tiene DiasRestantes menor o igual al siguiente.
    /// **Validates: Requirements 7.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Listado_Trials_Ordenado_Por_DiasRestantes_Ascendente()
    {
        // Generador: array de offsets (días desde hoy) para FechaProximoCorte, entre 3 y 8 elementos
        var genOffsets = Gen.Choose(3, 8).SelectMany(cant =>
            Gen.Choose(-10, 20).ArrayOf(cant));

        return Prop.ForAll(
            genOffsets.ToArbitrary(),
            (int[] offsets) =>
            {
                var (db, service) = CreateContext();
                using (db)
                {
                    var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
                    var estados = new[] { "Trial", "Trial_Expirado" };

                    // Sembrar comercios y suscripciones con datos variados
                    for (int i = 0; i < offsets.Length; i++)
                    {
                        var comercioId = i + 1;
                        var ruc = $"{comercioId:D13}"; // RUC único por comercio
                        var estado = estados[i % estados.Length]; // Alternar estados

                        var comercio = new Comercio
                        {
                            Id = comercioId,
                            Ruc = ruc,
                            RazonSocial = $"Comercio Test {comercioId}",
                            PlanId = 1,
                            Estado = "Activo",
                            FechaRegistro = DateTime.UtcNow.AddDays(-30)
                        };
                        db.Comercios.Add(comercio);

                        var suscripcion = new Suscripcion
                        {
                            ComercioId = comercioId,
                            PlanId = 1,
                            FechaInicio = hoy.AddDays(-15),
                            FechaProximoCorte = hoy.AddDays(offsets[i]),
                            MontoCuota = 0,
                            EsProporcional = false,
                            Estado = estado,
                            DiasExtendidos = 0
                        };
                        db.Suscripciones.Add(suscripcion);
                    }
                    db.SaveChanges();

                    // Ejecutar método bajo prueba
                    var resultado = service.ObtenerTrialsAsync().GetAwaiter().GetResult();

                    // Verificar que la lista tiene elementos
                    if (resultado.Count < 2)
                        return true.ToProperty();

                    // Verificar ordenamiento: cada DiasRestantes <= siguiente DiasRestantes
                    var ordenado = resultado
                        .Zip(resultado.Skip(1), (actual, siguiente) => actual.DiasRestantes <= siguiente.DiasRestantes)
                        .All(x => x);

                    return ordenado
                        .Label($"Resultado no ordenado. DiasRestantes: [{string.Join(", ", resultado.Select(r => r.DiasRestantes))}]");
                }
            });
    }
}
