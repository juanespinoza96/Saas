using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

// Feature: mejoras-operativas-v2, Property 6: Invariante de al menos un tipo de comprobante habilitado
/// <summary>
/// Property-based tests para el invariante de al menos un tipo de comprobante habilitado.
/// **Validates: Requirements 6.4**
///
/// Property 6: Invariante de al menos un tipo de comprobante habilitado
/// "For any configuración de comprobantes de un comercio, la operación de deshabilitar un tipo
/// SHALL ser rechazada si resultaría en 0 tipos habilitados. En todo momento, Count(tipos habilitados) >= 1."
/// </summary>
public class ComprobantesConfigInvariantePropertyTests
{
    private static readonly Mock<ILogger<ComprobantesConfigService>> LoggerMock = new();

    /// <summary>
    /// Tipos válidos de comprobante en el sistema.
    /// </summary>
    private static readonly string[] TiposValidos =
    {
        "Ticket Digital",
        "Ticket Impreso",
        "Factura Electrónica"
    };

    /// <summary>
    /// Crea un contexto InMemory con ITenantContext configurado como SuperAdmin
    /// para evitar query filters.
    /// </summary>
    private static (AppDbContext db, ComprobantesConfigService service) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);
        var service = new ComprobantesConfigService(db, LoggerMock.Object);
        return (db, service);
    }

    /// <summary>
    /// Configura un comercio con Plan Intermedio (para evitar restricciones de factura).
    /// </summary>
    private static Comercio SeedComercio(AppDbContext db, int comercioId = 1)
    {
        var plan = new Plan
        {
            Id = 1,
            Nombre = "Intermedio",
            Precio = 750m,
            LimiteUsuarios = 10,
            LimiteAtributos = 10,
            LimiteSucursales = 5
        };
        db.Planes.Add(plan);

        var comercio = new Comercio
        {
            Id = comercioId,
            Ruc = "0912345678001",
            RazonSocial = "Comercio Test",
            PlanId = 1,
            Plan = plan,
            Estado = "Activo",
            UsaFacturacionSRI = true,
            FechaRegistro = DateTime.UtcNow
        };
        db.Comercios.Add(comercio);
        db.SaveChanges();

        return comercio;
    }

    #region Property 6a: Deshabilitar último tipo habilitado es rechazado

    /// <summary>
    /// Property 6a: Cuando hay exactamente 1 tipo de comprobante habilitado,
    /// intentar deshabilitarlo debe retornar Result.Fail con código COMPROBANTE_MIN_ONE_REQUIRED.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DeshabilitarUltimoTipo_EsRechazado_ConCodigoCorrecto()
    {
        // Generador: seleccionar un tipo de comprobante como el único habilitado
        var tipoGen = Gen.Elements(TiposValidos);

        return Prop.ForAll(tipoGen.ToArbitrary(), (string tipoHabilitado) =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                SeedComercio(db);

                // Configurar exactamente 1 tipo habilitado
                db.ConfiguracionesComprobante.Add(new ConfiguracionComprobante
                {
                    Id = 1,
                    ComercioId = 1,
                    TipoComprobante = tipoHabilitado,
                    Habilitado = true,
                    FechaCreacion = DateTime.UtcNow
                });

                // Agregar otros tipos como deshabilitados
                var otrosTipos = TiposValidos.Where(t => t != tipoHabilitado).ToArray();
                for (int i = 0; i < otrosTipos.Length; i++)
                {
                    db.ConfiguracionesComprobante.Add(new ConfiguracionComprobante
                    {
                        Id = 10 + i,
                        ComercioId = 1,
                        TipoComprobante = otrosTipos[i],
                        Habilitado = false,
                        FechaCreacion = DateTime.UtcNow
                    });
                }
                db.SaveChanges();

                // Intentar deshabilitar el único tipo habilitado
                var result = service.DeshabilitarTipoAsync(1, tipoHabilitado)
                    .GetAwaiter().GetResult();

                // Debe fallar con el código correcto
                var rechazado = !result.Success && result.ErrorCode == "COMPROBANTE_MIN_ONE_REQUIRED";
                return rechazado.ToProperty()
                    .Label($"Deshabilitar último tipo '{tipoHabilitado}' debería ser rechazado: " +
                           $"Success={result.Success}, Code={result.ErrorCode}");
            }
        });
    }

    #endregion

    #region Property 6b: Deshabilitar con múltiples tipos habilitados sí se permite

    /// <summary>
    /// Property 6b: Cuando hay 2 o más tipos de comprobante habilitados,
    /// deshabilitar uno de ellos debe retornar Result.Ok().
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DeshabilitarConMultiplesHabilitados_EsPermitido()
    {
        // Generador: subconjunto de al menos 2 tipos habilitados + seleccionar cuál deshabilitar
        var gen = from numHabilitados in Gen.Choose(2, 3)
                  from indices in Gen.Shuffle(new[] { 0, 1, 2 })
                  let habilitados = indices.Take(numHabilitados).Select(i => TiposValidos[i]).ToArray()
                  from indiceDeshabilitar in Gen.Choose(0, numHabilitados - 1)
                  select (habilitados, habilitados[indiceDeshabilitar]);

        return Prop.ForAll(gen.ToArbitrary(), ((string[] habilitados, string tipoDeshabilitar) input) =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                SeedComercio(db);

                // Configurar los tipos habilitados
                int id = 1;
                foreach (var tipo in input.habilitados)
                {
                    db.ConfiguracionesComprobante.Add(new ConfiguracionComprobante
                    {
                        Id = id++,
                        ComercioId = 1,
                        TipoComprobante = tipo,
                        Habilitado = true,
                        FechaCreacion = DateTime.UtcNow
                    });
                }

                // Agregar tipos restantes como deshabilitados
                var noHabilitados = TiposValidos.Except(input.habilitados).ToArray();
                foreach (var tipo in noHabilitados)
                {
                    db.ConfiguracionesComprobante.Add(new ConfiguracionComprobante
                    {
                        Id = id++,
                        ComercioId = 1,
                        TipoComprobante = tipo,
                        Habilitado = false,
                        FechaCreacion = DateTime.UtcNow
                    });
                }
                db.SaveChanges();

                // Intentar deshabilitar uno de los múltiples tipos habilitados
                var result = service.DeshabilitarTipoAsync(1, input.tipoDeshabilitar)
                    .GetAwaiter().GetResult();

                return result.Success.ToProperty()
                    .Label($"Con {input.habilitados.Length} tipos habilitados ({string.Join(", ", input.habilitados)}), " +
                           $"deshabilitar '{input.tipoDeshabilitar}' debería permitirse pero falló: {result.ErrorCode}");
            }
        });
    }

    #endregion

    #region Property 6c: Invariante se mantiene después de cualquier operación de deshabilitar

    /// <summary>
    /// Property 6c: Después de ejecutar DeshabilitarTipoAsync (sin importar si tiene éxito o falla),
    /// siempre queda al menos 1 tipo habilitado en la base de datos.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InvarianteMinUnoHabilitado_SeMantieneSiempre()
    {
        // Generador: configuración aleatoria con al menos 1 tipo habilitado + tipo a deshabilitar
        var gen = from numHabilitados in Gen.Choose(1, 3)
                  from indices in Gen.Shuffle(new[] { 0, 1, 2 })
                  let habilitados = indices.Take(numHabilitados).Select(i => TiposValidos[i]).ToArray()
                  from tipoDeshabilitar in Gen.Elements(TiposValidos)
                  select (habilitados, tipoDeshabilitar);

        return Prop.ForAll(gen.ToArbitrary(), ((string[] habilitados, string tipoDeshabilitar) input) =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                SeedComercio(db);

                // Configurar todos los tipos con su estado
                int id = 1;
                foreach (var tipo in TiposValidos)
                {
                    db.ConfiguracionesComprobante.Add(new ConfiguracionComprobante
                    {
                        Id = id++,
                        ComercioId = 1,
                        TipoComprobante = tipo,
                        Habilitado = input.habilitados.Contains(tipo),
                        FechaCreacion = DateTime.UtcNow
                    });
                }
                db.SaveChanges();

                // Ejecutar la operación (puede tener éxito o fallar)
                _ = service.DeshabilitarTipoAsync(1, input.tipoDeshabilitar)
                    .GetAwaiter().GetResult();

                // Verificar el invariante: siempre al menos 1 habilitado
                var habilitadosCount = db.ConfiguracionesComprobante
                    .Count(c => c.ComercioId == 1 && c.Habilitado);

                return (habilitadosCount >= 1).ToProperty()
                    .Label($"Invariante violado: {habilitadosCount} tipos habilitados después de intentar deshabilitar " +
                           $"'{input.tipoDeshabilitar}' (config inicial: {string.Join(", ", input.habilitados)})");
            }
        });
    }

    #endregion
}
