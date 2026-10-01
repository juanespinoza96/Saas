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

/// <summary>
/// Tests de propiedades para TimezoneResolver (cascada de resolución de zona horaria).
/// Valida que la prioridad de resolución se respeta en todas las combinaciones posibles.
/// </summary>
public class TimezoneResolverPropertyTests
{
    /// <summary>
    /// Zonas IANA válidas conocidas para usar en los generadores.
    /// </summary>
    private static readonly string[] ZonasValidas =
    [
        "America/Guayaquil",
        "America/Bogota",
        "Europe/Madrid",
        "Asia/Tokyo",
        "UTC",
        "America/New_York",
        "Europe/London",
        "America/Lima"
    ];

    /// <summary>
    /// Strings inválidos que no representan zonas IANA reconocidas.
    /// </summary>
    private static readonly string[] ZonasInvalidas =
    [
        "Invalid/Zone",
        "Foo/Bar",
        "NoExiste",
        "123/456",
        "XYZABC"
    ];

    #region Generadores

    /// <summary>
    /// Genera un valor de fuente de timezone: puede ser una zona válida, una zona inválida, null o vacío.
    /// </summary>
    private static Gen<string?> GenFuenteTimezone()
    {
        var genValida = Gen.Elements(ZonasValidas);
        var genInvalida = Gen.Elements(ZonasInvalidas);
        var genNull = Gen.Constant<string?>(null);
        var genVacio = Gen.Elements("", " ", "  ");

        return Gen.OneOf(
            genValida.Select<string, string?>(s => s),
            genInvalida.Select<string, string?>(s => s),
            genNull,
            genVacio.Select<string, string?>(s => s)
        );
    }

    /// <summary>
    /// Genera una tupla de 3 fuentes de timezone (header, usuario, comercio).
    /// </summary>
    private static Arbitrary<(string? Header, string? Usuario, string? Comercio)> ArbitraryCascadaInput()
    {
        var gen = GenFuenteTimezone().SelectMany(header =>
            GenFuenteTimezone().SelectMany(usuario =>
                GenFuenteTimezone().Select(comercio =>
                    (Header: header, Usuario: usuario, Comercio: comercio)
                )
            )
        );

        return Arb.From(gen);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Crea un AppDbContext InMemory con datos de prueba semilla.
    /// </summary>
    private static (AppDbContext db, Mock<ITenantContext> tenantMock) CrearDbConDatos(
        string? zonaUsuario,
        string? zonaComercio)
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        // Sembrar Plan (requerido por Comercio)
        var plan = new Plan
        {
            Id = 1,
            Nombre = "Básico",
            Precio = 9.99m,
            LimiteUsuarios = 5,
            LimiteAtributos = 5,
            LimiteSucursales = 1
        };
        db.Planes.Add(plan);

        // Sembrar Comercio
        var comercio = new Comercio
        {
            Id = 1,
            Ruc = "0990001234001",
            RazonSocial = "Test Comercio",
            PlanId = 1,
            FechaRegistro = DateTime.UtcNow,
            Estado = "Activo",
            ZonaHorariaDefecto = zonaComercio
        };
        db.Comercios.Add(comercio);

        // Sembrar Usuario
        var usuario = new Usuario
        {
            Id = 1,
            ComercioId = 1,
            Nombre = "Test Usuario",
            Email = "test@test.com",
            PasswordHash = "hash",
            Rol = "Dueño",
            ZonaHoraria = zonaUsuario
        };
        db.Usuarios.Add(usuario);

        db.SaveChanges();

        return (db, tenantContextMock);
    }

    /// <summary>
    /// Determina si un string es una zona IANA válida según TimeZoneInfo.
    /// </summary>
    private static bool EsZonaValida(string? zona)
    {
        if (string.IsNullOrWhiteSpace(zona))
            return false;

        return TimeZoneInfo.TryFindSystemTimeZoneById(zona, out _);
    }

    /// <summary>
    /// Calcula el resultado esperado de la cascada de resolución de timezone.
    /// Prioridad: 1) header, 2) usuario, 3) comercio, 4) "UTC"
    /// Cada fuente solo se acepta si es una zona IANA válida y no es null/vacío.
    /// </summary>
    private static string ResultadoEsperado(string? header, string? zonaUsuario, string? zonaComercio)
    {
        // 1) Header: si es válido, retornar
        if (EsZonaValida(header))
            return header!;

        // 2) Usuario: si es válido, retornar
        if (EsZonaValida(zonaUsuario))
            return zonaUsuario!;

        // 3) Comercio: si es válido, retornar
        if (EsZonaValida(zonaComercio))
            return zonaComercio!;

        // 4) Fallback: UTC
        return "UTC";
    }

    #endregion

    #region Property 2: Cascada de resolución de timezone respeta prioridad

    /// <summary>
    /// Property 2: Cascada de resolución de timezone respeta prioridad.
    /// Para cualquier combinación de valores (headerTimezone, usuario.ZonaHoraria, comercio.ZonaHorariaDefecto)
    /// donde cada uno puede ser una zona IANA válida, una zona inválida, null o vacío,
    /// el TimezoneResolver SHALL retornar la primera fuente válida no-nula según el orden:
    /// 1) headerTimezone, 2) usuario.ZonaHoraria, 3) comercio.ZonaHorariaDefecto, 4) "UTC".
    ///
    /// **Validates: Requirements 2.2, 2.3, 2.5, 8.2, 8.5, 8.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CascadaResolucion_RespetaPrioridad()
    {
        return Prop.ForAll(ArbitraryCascadaInput(), tuple =>
        {
            var (header, zonaUsuario, zonaComercio) = tuple;

            // Crear DB con datos de prueba
            var (db, _) = CrearDbConDatos(zonaUsuario, zonaComercio);

            try
            {
                // Crear TimezoneService real para validación
                var loggerServiceMock = new Mock<ILogger<TimezoneService>>();
                var timezoneService = new TimezoneService(loggerServiceMock.Object);

                // Crear TimezoneResolver
                var loggerResolverMock = new Mock<ILogger<TimezoneResolver>>();
                var resolver = new TimezoneResolver(db, timezoneService, loggerResolverMock.Object);

                // Ejecutar resolución (async → sincronizar con .Result)
                var resultado = resolver.ResolveAsync(
                    usuarioId: 1,
                    comercioId: 1,
                    headerTimezone: header
                ).GetAwaiter().GetResult();

                // Calcular resultado esperado según la cascada
                var esperado = ResultadoEsperado(header, zonaUsuario, zonaComercio);

                return resultado == esperado;
            }
            finally
            {
                db.Dispose();
            }
        });
    }

    #endregion
}
