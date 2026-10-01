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

// Feature: mejoras-operativas-v2, Property 8: Validación API rechaza tipos de comprobante no habilitados
/// <summary>
/// Property-based tests para la validación de tipos de comprobante en la API de ventas.
/// **Validates: Requirements 6.10**
///
/// Property 8: Validación API rechaza tipos de comprobante no habilitados
/// "For any tipo de comprobante T enviado en una solicitud de venta y for any conjunto de tipos
/// habilitados H del comercio, la API SHALL aceptar la venta si T ∈ H y retornar HTTP 400
/// con código TIPO_COMPROBANTE_NO_HABILITADO si T ∉ H."
///
/// Se valida que EsTipoHabilitadoAsync retorna true si el tipo está habilitado y false si no,
/// lo cual determina si la API acepta o rechaza la venta.
/// </summary>
public class ValidacionApiTipoComprobantePropertyTests
{
    private static readonly Mock<ILogger<ComprobantesConfigService>> LoggerMock = new();

    /// <summary>
    /// Todos los tipos válidos de comprobante en el sistema.
    /// </summary>
    private static readonly string[] TodosLosTipos =
    {
        "Ticket Digital",
        "Ticket Impreso",
        "Factura Electrónica"
    };

    /// <summary>
    /// Crea un contexto InMemory con ITenantContext configurado como SuperAdmin
    /// para evitar query filters de multi-tenancy.
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
    /// Configura un comercio con Plan Intermedio y facturación SRI habilitada.
    /// </summary>
    private static void SeedComercio(AppDbContext db, int comercioId = 1)
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
    }

    #region Property 8a: Tipo habilitado es aceptado (T ∈ H → true)

    /// <summary>
    /// Property 8a: Para cualquier subconjunto no vacío de tipos habilitados H y
    /// cualquier tipo T que pertenezca a H, EsTipoHabilitadoAsync retorna true.
    /// Esto implica que la API aceptaría la venta.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TipoHabilitado_EsAceptado_PorElServicio()
    {
        // Generador: subconjunto no vacío de tipos habilitados + seleccionar un tipo de ese subconjunto
        var gen = from numHabilitados in Gen.Choose(1, 3)
                  from indices in Gen.Shuffle(new[] { 0, 1, 2 })
                  let habilitados = indices.Take(numHabilitados).Select(i => TodosLosTipos[i]).ToArray()
                  from indiceTipo in Gen.Choose(0, numHabilitados - 1)
                  select (habilitados, habilitados[indiceTipo]);

        return Prop.ForAll(gen.ToArbitrary(), ((string[] habilitados, string tipoConsultado) input) =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                SeedComercio(db);

                // Configurar tipos: habilitados según el subconjunto generado
                int id = 1;
                foreach (var tipo in TodosLosTipos)
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

                // Verificar que el tipo habilitado es aceptado
                var esHabilitado = service.EsTipoHabilitadoAsync(1, input.tipoConsultado)
                    .GetAwaiter().GetResult();

                return esHabilitado.ToProperty()
                    .Label($"Tipo '{input.tipoConsultado}' debería ser aceptado (habilitados: [{string.Join(", ", input.habilitados)}])");
            }
        });
    }

    #endregion

    #region Property 8b: Tipo no habilitado es rechazado (T ∉ H → false)

    /// <summary>
    /// Property 8b: Para cualquier subconjunto no vacío de tipos habilitados H y
    /// cualquier tipo T que NO pertenezca a H, EsTipoHabilitadoAsync retorna false.
    /// Esto implica que la API retornaría HTTP 400 con código TIPO_COMPROBANTE_NO_HABILITADO.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TipoNoHabilitado_EsRechazado_PorElServicio()
    {
        // Generador: subconjunto estricto de tipos (1 o 2) habilitados + seleccionar un tipo que NO está en ese subconjunto
        var gen = from numHabilitados in Gen.Choose(1, 2)
                  from indices in Gen.Shuffle(new[] { 0, 1, 2 })
                  let habilitados = indices.Take(numHabilitados).Select(i => TodosLosTipos[i]).ToArray()
                  let noHabilitados = TodosLosTipos.Where(t => !habilitados.Contains(t)).ToArray()
                  from indiceNoHabilitado in Gen.Choose(0, noHabilitados.Length - 1)
                  select (habilitados, noHabilitados[indiceNoHabilitado]);

        return Prop.ForAll(gen.ToArbitrary(), ((string[] habilitados, string tipoConsultado) input) =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                SeedComercio(db);

                // Configurar tipos: habilitados según el subconjunto generado
                int id = 1;
                foreach (var tipo in TodosLosTipos)
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

                // Verificar que el tipo no habilitado es rechazado
                var esHabilitado = service.EsTipoHabilitadoAsync(1, input.tipoConsultado)
                    .GetAwaiter().GetResult();

                return (!esHabilitado).ToProperty()
                    .Label($"Tipo '{input.tipoConsultado}' debería ser rechazado (habilitados: [{string.Join(", ", input.habilitados)}])");
            }
        });
    }

    #endregion

    #region Property 8c: Tipo inexistente (no configurado) es rechazado

    /// <summary>
    /// Property 8c: Para cualquier tipo de comprobante T que no existe en la configuración del comercio,
    /// EsTipoHabilitadoAsync retorna false, es decir, la API rechazaría la venta.
    /// Esto cubre el caso donde se envía un tipo arbitrario no reconocido.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TipoInexistente_EsRechazado_PorElServicio()
    {
        // Generador: tipos arbitrarios que no están en la lista de tipos válidos del sistema
        var tiposInvalidos = new[] { "Recibo", "Nota de Venta", "Boleta", "Comprobante Fiscal", "Invoice", "" };
        var tipoGen = Gen.Elements(tiposInvalidos).Where(t => !string.IsNullOrWhiteSpace(t));

        // Generador de subconjunto habilitado (al menos 1 tipo válido)
        var gen = from numHabilitados in Gen.Choose(1, 3)
                  from indices in Gen.Shuffle(new[] { 0, 1, 2 })
                  let habilitados = indices.Take(numHabilitados).Select(i => TodosLosTipos[i]).ToArray()
                  from tipoInvalido in tipoGen
                  select (habilitados, tipoInvalido);

        return Prop.ForAll(gen.ToArbitrary(), ((string[] habilitados, string tipoConsultado) input) =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                SeedComercio(db);

                // Configurar solo tipos válidos
                int id = 1;
                foreach (var tipo in TodosLosTipos)
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

                // Verificar que un tipo inexistente/inválido es rechazado
                var esHabilitado = service.EsTipoHabilitadoAsync(1, input.tipoConsultado)
                    .GetAwaiter().GetResult();

                return (!esHabilitado).ToProperty()
                    .Label($"Tipo inexistente '{input.tipoConsultado}' debería ser rechazado " +
                           $"(habilitados: [{string.Join(", ", input.habilitados)}])");
            }
        });
    }

    #endregion

    #region Property 8d: Normalización legacy integrada en validación

    /// <summary>
    /// Property 8d: Cuando se envía un valor legacy "Ticket Interno" y "Ticket Digital" está habilitado,
    /// la validación acepta la venta (el servicio normaliza antes de verificar).
    /// Cuando "Ticket Digital" no está habilitado, la venta se rechaza incluso con el valor legacy.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TipoLegacy_NormalizadoAntesDeValidar()
    {
        // Generar configuraciones donde "Ticket Digital" está o no habilitado
        var ticketDigitalHabilitadoGen = Gen.Elements(true, false);

        return Prop.ForAll(ticketDigitalHabilitadoGen.ToArbitrary(), (bool ticketDigitalHabilitado) =>
        {
            var (db, service) = CreateContext();
            using (db)
            {
                SeedComercio(db);

                // Configurar tipos: Ticket Digital según la variable generada,
                // al menos uno habilitado para cumplir invariantes
                int id = 1;
                db.ConfiguracionesComprobante.Add(new ConfiguracionComprobante
                {
                    Id = id++,
                    ComercioId = 1,
                    TipoComprobante = "Ticket Digital",
                    Habilitado = ticketDigitalHabilitado,
                    FechaCreacion = DateTime.UtcNow
                });
                db.ConfiguracionesComprobante.Add(new ConfiguracionComprobante
                {
                    Id = id++,
                    ComercioId = 1,
                    TipoComprobante = "Ticket Impreso",
                    Habilitado = true, // Siempre uno habilitado para invariante
                    FechaCreacion = DateTime.UtcNow
                });
                db.ConfiguracionesComprobante.Add(new ConfiguracionComprobante
                {
                    Id = id++,
                    ComercioId = 1,
                    TipoComprobante = "Factura Electrónica",
                    Habilitado = false,
                    FechaCreacion = DateTime.UtcNow
                });
                db.SaveChanges();

                // Enviar el valor legacy "Ticket Interno" (que normaliza a "Ticket Digital")
                var esHabilitado = service.EsTipoHabilitadoAsync(1, "Ticket Interno")
                    .GetAwaiter().GetResult();

                // Debe coincidir con el estado de "Ticket Digital"
                return (esHabilitado == ticketDigitalHabilitado).ToProperty()
                    .Label($"'Ticket Interno' (→ 'Ticket Digital') con habilitado={ticketDigitalHabilitado}: " +
                           $"EsTipoHabilitado retornó {esHabilitado}");
            }
        });
    }

    #endregion
}
