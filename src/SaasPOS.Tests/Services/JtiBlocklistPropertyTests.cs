using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Feature: endurecimiento-esquema-y-revocacion-jti, tarea 10.2.
/// Tests basados en propiedades (FsCheck.Xunit, ≥100 iteraciones por propiedad) de las
/// invariantes de la revocación individual de JTI implementada en <see cref="JtiBlocklist"/>
/// (tareas 8.1/8.2: persistencia en BD + caché de corta vida con auto-expiración lógica).
///
/// Property 1: un jti revocado dentro de su vigencia siempre se reporta bloqueado; uno cuya
///             expiración ya pasó, nunca (auto-expiración lógica: IsBlocked respeta
///             Expiracion &gt; UtcNow). Además, por Req 8.3, revocar con una expiración ya
///             pasada no persiste nada, por lo que también se reporta no bloqueado.
/// Property 2: AddToBlocklist es idempotente: revocar el mismo jti N veces (N ≥ 1) no cambia
///             el resultado observable de IsBlocked ni lanza por el índice único.
///
/// Aislamiento entre iteraciones: FsCheck reejecuta el cuerpo muchas veces, así que CADA
/// iteración construye un arnés FRESCO (base EF Core InMemory con nombre único por GUID +
/// IMemoryCache nueva). Así no hay estado residual ni colisiones de jti entre ejecuciones.
/// </summary>
public class JtiBlocklistPropertyTests
{
    /// <summary>
    /// Arnés autocontenido por iteración: el proveedor de servicios (dueño del AppDbContext
    /// Scoped sobre una base InMemory única), la caché real, un AppDbContext de control sobre la
    /// MISMA base para sembrar/leer filas, y el SUT <see cref="JtiBlocklist"/> ya cableado.
    /// Idéntico patrón al de los unit tests (tarea 10.1), replicado aquí para que cada iteración
    /// de FsCheck parta de un estado limpio.
    /// </summary>
    private sealed class Arnes : IDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required MemoryCache Cache { get; init; }
        public required AppDbContext ControlDb { get; init; }
        public required JtiBlocklist Sut { get; init; }

        public void Dispose()
        {
            ControlDb.Dispose();
            Cache.Dispose();
            Provider.Dispose();
        }
    }

    // Construye un arnés fresco: ServiceProvider real con AppDbContext InMemory (nombre único →
    // base aislada por iteración), un ITenantContext SuperAdmin (JtiRevocados no usa filtros de
    // tenant), IMemoryCache real y el JtiBlocklist bajo prueba.
    private static Arnes CrearArnes()
    {
        var nombreBase = Guid.NewGuid().ToString();

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var services = new ServiceCollection();
        services.AddSingleton(tenantContextMock.Object);
        services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(nombreBase));

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new JtiBlocklist(cache, scopeFactory, NullLogger<JtiBlocklist>.Instance);

        var controlOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nombreBase)
            .Options;
        var controlDb = new AppDbContext(controlOptions, tenantContextMock.Object);

        return new Arnes
        {
            Provider = provider,
            Cache = cache,
            ControlDb = controlDb,
            Sut = sut,
        };
    }

    // Normaliza el jti arbitrario de FsCheck a una cadena no vacía estable: un jti vacío/nulo
    // tiene un comportamiento aparte (Req 8.3) que cubren los unit tests, no estas propiedades.
    private static string JtiNoVacio(string? generado)
        => string.IsNullOrEmpty(generado) ? "jti-fallback" : generado;

    // Feature: endurecimiento-esquema-y-revocacion-jti, Property 1:
    // Un jti revocado dentro de su vigencia siempre se reporta bloqueado; uno expirado nunca.
    /// <summary>
    /// Para un jti arbitrario y un desplazamiento de segundos arbitrario respecto a "ahora",
    /// <c>IsBlocked</c> respeta la auto-expiración lógica: devuelve <c>true</c> si y solo si la
    /// expiración del token es futura (Expiracion &gt; UtcNow). Se usa un margen de ≥60 s para
    /// evitar la ambigüedad del instante exacto de "ahora" (el reloj avanza entre revocar y
    /// consultar). El caso de expiración pasada cubre también el Req 8.3 (no se persiste) cuando
    /// se revoca vía <c>AddToBlocklist</c>, y la auto-expiración lógica (Req 8.6) cuando la fila
    /// se siembra directamente con expiración ya vencida.
    /// **Validates: Requirements 8.2, 8.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Property1_JtiVigenteBloqueado_ExpiradoNoBloqueado(string jtiGenerado, PositiveInt segundos, bool futuro)
    {
        using var arnes = CrearArnes();
        var jti = JtiNoVacio(jtiGenerado);

        // Margen de al menos 60 s para que el signo del offset no dependa del avance del reloj.
        var offset = TimeSpan.FromSeconds(60 + (segundos.Get % 3600));
        var expiracion = futuro
            ? DateTime.UtcNow.Add(offset)   // vigencia futura
            : DateTime.UtcNow.Subtract(offset); // ya expirado

        if (futuro)
        {
            // Revocación normal: token vigente → debe quedar bloqueado.
            arnes.Sut.AddToBlocklist(jti, expiracion);
            return arnes.Sut.IsBlocked(jti);
        }

        // Caso expirado por dos vías equivalentes en resultado observable:
        //  a) AddToBlocklist con expiración pasada NO persiste (Req 8.3) → no bloqueado.
        arnes.Sut.AddToBlocklist(jti, expiracion);
        var viaAdd = arnes.Sut.IsBlocked(jti);

        //  b) Fila sembrada directamente con Expiracion vencida → auto-expiración lógica
        //     (Req 8.6): IsBlocked == false aunque la fila exista. Se usa otro jti y otro
        //     arnés para no mezclar la caché negativa poblada por (a).
        using var arnes2 = CrearArnes();
        var jti2 = jti + "-sembrado";
        arnes2.ControlDb.JtiRevocados.Add(new JtiRevocado
        {
            Jti = jti2,
            Expiracion = expiracion,
            FechaRevocacion = DateTime.UtcNow.Subtract(offset),
        });
        arnes2.ControlDb.SaveChanges();
        var viaSembrado = arnes2.Sut.IsBlocked(jti2);

        return !viaAdd && !viaSembrado;
    }

    // Feature: endurecimiento-esquema-y-revocacion-jti, Property 2:
    // AddToBlocklist es idempotente.
    /// <summary>
    /// Revocar el mismo jti vigente N veces (N ≥ 1) no cambia el resultado observable de
    /// <c>IsBlocked</c> (siempre <c>true</c>), no lanza excepción por el índice único y deja
    /// exactamente una fila en <c>JtiRevocados</c>. Se construye un arnés fresco por iteración.
    /// **Validates: Requirements 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Property2_AddToBlocklist_EsIdempotente(string jtiGenerado, PositiveInt repeticionesGeneradas)
    {
        using var arnes = CrearArnes();
        var jti = JtiNoVacio(jtiGenerado);

        // N en [1, 10]: revocar más de una vez debe ser idempotente.
        var n = 1 + (repeticionesGeneradas.Get % 10);
        var expiracion = DateTime.UtcNow.AddHours(1);

        for (var i = 0; i < n; i++)
        {
            // No debe lanzar por el índice único en ninguna repetición.
            arnes.Sut.AddToBlocklist(jti, expiracion);
        }

        // Resultado observable estable: bloqueado.
        var bloqueado = arnes.Sut.IsBlocked(jti);
        // Exactamente una fila persistida pese a las N revocaciones (idempotencia en BD).
        var unaSolaFila = arnes.ControlDb.JtiRevocados.Count(j => j.Jti == jti) == 1;

        return bloqueado && unaSolaFila;
    }
}
