using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;
using Xunit;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Feature: endurecimiento-esquema-y-revocacion-jti, tarea 10.1.
/// Unit tests (xUnit + Moq) de la caché de revocación individual de <see cref="JtiBlocklist"/>.
///
/// Cubren el comportamiento añadido en las tareas 8.1/8.2 (persistencia en BD + caché de 30 s):
///   - No persistir tokens ya expirados (Req 8.3).
///   - Acierto/fallo de caché y poblado con el TTL acotado a min(Expiracion, Now+30s) (Req 8.4, 8.5).
///   - Auto-expiración lógica: una fila con Expiracion ya pasada → IsBlocked == false (Req 8.6).
///   - Cortes masivos por usuario/comercio intactos y 100% en memoria (Req 9.1, 9.2).
///
/// <see cref="JtiBlocklist"/> es Singleton y abre scopes efímeros vía <see cref="IServiceScopeFactory"/>
/// para resolver un <see cref="AppDbContext"/> por operación individual. En los tests se construye un
/// <see cref="IServiceScopeFactory"/> REAL (vía <see cref="ServiceCollection"/>) que resuelve un
/// AppDbContext EF Core InMemory apuntando siempre a la MISMA base (identificada por un nombre fijo
/// por test), de modo que todos los scopes efímeros ven los mismos datos. La <see cref="IMemoryCache"/>
/// es real (<c>new MemoryCache(...)</c>) para ejercitar el acierto/fallo y el TTL reales.
/// </summary>
public class JtiBlocklistTests
{
    /// <summary>
    /// Arnés autocontenido: el proveedor de servicios (dueño del AppDbContext Scoped sobre una base
    /// InMemory única), la caché real, un AppDbContext de control sobre la MISMA base para sembrar/leer
    /// filas directamente, y el SUT <see cref="JtiBlocklist"/> ya cableado.
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

    // Prefijo de la clave de caché de la revocación individual (debe coincidir con JtiBlocklist).
    private const string BlockedPrefix = "jti:blocked:";

    // Construye el arnés: ServiceProvider real con AppDbContext InMemory (nombre fijo → base compartida
    // entre todos los scopes), un ITenantContext SuperAdmin (sin filtros de tenant; JtiRevocados no los usa),
    // IMemoryCache real y el JtiBlocklist bajo prueba.
    private static Arnes CrearArnes()
    {
        var nombreBase = Guid.NewGuid().ToString();

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var services = new ServiceCollection();
        services.AddSingleton(tenantContextMock.Object);
        // AppDbContext Scoped sobre una base InMemory identificada por nombre: cada scope efímero que
        // abre JtiBlocklist resuelve un contexto nuevo, pero todos comparten el mismo almacén en memoria.
        services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(nombreBase));

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new JtiBlocklist(cache, scopeFactory, NullLogger<JtiBlocklist>.Instance);

        // AppDbContext de control sobre la MISMA base, para sembrar/leer filas directamente en los tests.
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 8.3: No persistir tokens ya expirados.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Si <c>AddToBlocklist</c> recibe una expiración ya pasada, no se inserta ninguna fila en
    /// <c>JtiRevocados</c> ni se cachea el jti como bloqueado (preserva el comportamiento previo).
    /// **Validates: Requirements 8.3**
    /// </summary>
    [Fact]
    public void AddToBlocklist_ExpiracionPasada_NoPersisteNiCachea()
    {
        using var arnes = CrearArnes();
        const string jti = "jti-expirado";

        // Expiración en el pasado → el token ya no es válido, no debe revocarse.
        arnes.Sut.AddToBlocklist(jti, DateTime.UtcNow.AddMinutes(-5));

        // No se insertó fila en la tabla.
        Assert.Equal(0, arnes.ControlDb.JtiRevocados.Count());
        // Tampoco quedó cacheado como bloqueado.
        Assert.False(arnes.Cache.TryGetValue($"{BlockedPrefix}{jti}", out bool _));
    }

    /// <summary>
    /// Entrada nula o vacía: <c>AddToBlocklist</c> no persiste y <c>IsBlocked</c> devuelve false
    /// sin tocar la BD. **Validates: Requirements 8.3**
    /// </summary>
    [Fact]
    public void AddToBlocklist_JtiVacioONulo_NoPersiste_IsBlockedFalse()
    {
        using var arnes = CrearArnes();

        arnes.Sut.AddToBlocklist("", DateTime.UtcNow.AddHours(1));
        arnes.Sut.AddToBlocklist(null!, DateTime.UtcNow.AddHours(1));

        Assert.Equal(0, arnes.ControlDb.JtiRevocados.Count());
        Assert.False(arnes.Sut.IsBlocked(""));
        Assert.False(arnes.Sut.IsBlocked(null!));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 8.4, 8.5: Acierto/fallo de caché y poblado del valor.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Primer <c>IsBlocked</c> sobre un jti vigente revocado es un FALLO de caché: consulta la BD,
    /// devuelve true y puebla la caché. Un segundo <c>IsBlocked</c> es un ACIERTO de caché: devuelve
    /// el valor cacheado SIN volver a la BD (se demuestra borrando la fila entre ambas llamadas y
    /// comprobando que la segunda sigue devolviendo true). **Validates: Requirements 8.4, 8.5**
    /// </summary>
    [Fact]
    public void IsBlocked_FalloLuegoAcierto_PoblaCacheYNoRevisitaBd()
    {
        using var arnes = CrearArnes();
        const string jti = "jti-vigente";

        // Fila vigente (Expiracion futura) directamente en la base.
        arnes.ControlDb.JtiRevocados.Add(new JtiRevocado
        {
            Jti = jti,
            Expiracion = DateTime.UtcNow.AddHours(1),
            FechaRevocacion = DateTime.UtcNow,
        });
        arnes.ControlDb.SaveChanges();

        // 1) Fallo de caché → consulta BD, true, puebla caché.
        Assert.True(arnes.Sut.IsBlocked(jti));
        Assert.True(arnes.Cache.TryGetValue($"{BlockedPrefix}{jti}", out bool cacheado) && cacheado);

        // Borramos la fila de la BD: si la 2ª llamada acertara en caché, no debería notar el borrado.
        var fila = arnes.ControlDb.JtiRevocados.Single(j => j.Jti == jti);
        arnes.ControlDb.JtiRevocados.Remove(fila);
        arnes.ControlDb.SaveChanges();

        // 2) Acierto de caché → sigue devolviendo true pese a que la BD ya no tiene la fila.
        Assert.True(arnes.Sut.IsBlocked(jti));
    }

    /// <summary>
    /// <c>IsBlocked</c> sobre un jti que no existe en BD devuelve false (fallo de caché) y cachea el
    /// valor negativo: una segunda consulta acierta en caché y sigue devolviendo false aunque se
    /// inserte la fila después (demuestra el poblado del negativo con TTL de 30 s).
    /// **Validates: Requirements 8.5**
    /// </summary>
    [Fact]
    public void IsBlocked_NoRevocado_CacheaNegativoYAcierta()
    {
        using var arnes = CrearArnes();
        const string jti = "jti-no-revocado";

        // 1) Fallo de caché → BD sin fila → false, cachea el negativo.
        Assert.False(arnes.Sut.IsBlocked(jti));
        Assert.True(arnes.Cache.TryGetValue($"{BlockedPrefix}{jti}", out bool cacheado) && cacheado == false);

        // Insertamos la fila DESPUÉS de cachear el negativo.
        arnes.ControlDb.JtiRevocados.Add(new JtiRevocado
        {
            Jti = jti,
            Expiracion = DateTime.UtcNow.AddHours(1),
            FechaRevocacion = DateTime.UtcNow,
        });
        arnes.ControlDb.SaveChanges();

        // 2) Acierto de caché del negativo → sigue false dentro de la ventana de 30 s.
        Assert.False(arnes.Sut.IsBlocked(jti));
    }

    /// <summary>
    /// <c>AddToBlocklist</c> de un jti vigente persiste una única fila y puebla la caché como
    /// bloqueado de inmediato, de modo que <c>IsBlocked</c> devuelve true sin necesidad de volver a
    /// la BD. **Validates: Requirements 8.2, 8.4**
    /// </summary>
    [Fact]
    public void AddToBlocklist_Vigente_PersisteUnaFilaYCacheaBloqueado()
    {
        using var arnes = CrearArnes();
        const string jti = "jti-a-revocar";

        arnes.Sut.AddToBlocklist(jti, DateTime.UtcNow.AddHours(1));

        // Se insertó exactamente una fila con los datos esperados.
        var fila = Assert.Single(arnes.ControlDb.JtiRevocados.Where(j => j.Jti == jti));
        Assert.True(fila.Expiracion > DateTime.UtcNow);
        // Caché poblada como bloqueado tras AddToBlocklist.
        Assert.True(arnes.Cache.TryGetValue($"{BlockedPrefix}{jti}", out bool cacheado) && cacheado);
        // IsBlocked true (acierto de caché).
        Assert.True(arnes.Sut.IsBlocked(jti));
    }

    /// <summary>
    /// El TTL de la entrada de caché se acota a <c>min(Expiracion, Now+30s)</c>. Con una
    /// <c>Expiracion</c> muy corta (≈1 s, menor que los 30 s de TTL fijo), la entrada de caché
    /// bloqueada debe vencer poco después de esa <c>Expiracion</c>, no a los 30 s.
    /// **Validates: Requirements 8.4**
    /// </summary>
    [Fact]
    public async Task AddToBlocklist_TtlAcotadoALaExpiracionCorta()
    {
        using var arnes = CrearArnes();
        const string jti = "jti-ttl-corto";

        // Expiracion a ~1 s en el futuro: min(Expiracion, Now+30s) == Expiracion (~1 s).
        arnes.Sut.AddToBlocklist(jti, DateTime.UtcNow.AddSeconds(1));

        // Inmediatamente está en caché como bloqueado.
        Assert.True(arnes.Cache.TryGetValue($"{BlockedPrefix}{jti}", out bool _));

        // Pasado el vencimiento acotado (~1 s), la entrada ya expiró del caché (no duró 30 s).
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(arnes.Cache.TryGetValue($"{BlockedPrefix}{jti}", out bool _));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 8.6: Auto-expiración lógica.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un jti cuya fila tiene <c>Expiracion</c> ya pasada se trata como NO bloqueado: <c>IsBlocked</c>
    /// devuelve false aunque la fila siga físicamente en la tabla (hasta la limpieza periódica).
    /// **Validates: Requirements 8.6**
    /// </summary>
    [Fact]
    public void IsBlocked_FilaConExpiracionPasada_DevuelveFalseAunqueExistaFila()
    {
        using var arnes = CrearArnes();
        const string jti = "jti-fila-vencida";

        // Fila con Expiracion en el pasado (simula una revocación cuyo token ya caducó).
        arnes.ControlDb.JtiRevocados.Add(new JtiRevocado
        {
            Jti = jti,
            Expiracion = DateTime.UtcNow.AddMinutes(-1),
            FechaRevocacion = DateTime.UtcNow.AddMinutes(-10),
        });
        arnes.ControlDb.SaveChanges();

        // Auto-expiración lógica: NO bloqueado pese a existir la fila.
        Assert.False(arnes.Sut.IsBlocked(jti));
        // La fila sigue en la tabla (solo la limpieza periódica la borra).
        Assert.Equal(1, arnes.ControlDb.JtiRevocados.Count(j => j.Jti == jti));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Req 9.1, 9.2: Cortes masivos intactos y 100% en memoria (sin tocar BD).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>BlockAllForUser</c>/<c>IsUserBlocked</c> siguen operando sobre <c>IMemoryCache</c> sin tocar
    /// la BD: tras bloquear al usuario, un token emitido antes del bloqueo se considera bloqueado y uno
    /// emitido después, no; y no se escribe ninguna fila en <c>JtiRevocados</c>.
    /// **Validates: Requirements 9.1, 9.2**
    /// </summary>
    [Fact]
    public void BlockAllForUser_OperaEnMemoria_SinTocarBd()
    {
        using var arnes = CrearArnes();
        const int userId = 42;

        var antesDelBloqueo = DateTime.UtcNow.AddMinutes(-1);

        arnes.Sut.BlockAllForUser(userId);

        var despuesDelBloqueo = DateTime.UtcNow.AddMinutes(1);

        // Token emitido antes del corte → bloqueado; emitido después → no bloqueado.
        Assert.True(arnes.Sut.IsUserBlocked(userId, antesDelBloqueo));
        Assert.False(arnes.Sut.IsUserBlocked(userId, despuesDelBloqueo));
        // Usuario sin corte → nunca bloqueado.
        Assert.False(arnes.Sut.IsUserBlocked(999, antesDelBloqueo));
        // El corte masivo NO escribe en la tabla de revocación individual.
        Assert.Equal(0, arnes.ControlDb.JtiRevocados.Count());
    }

    /// <summary>
    /// <c>BlockAllForComercio</c>/<c>IsComercioBlocked</c> siguen operando sobre <c>IMemoryCache</c>
    /// sin tocar la BD, con la misma semántica de "emitido antes del corte → bloqueado".
    /// **Validates: Requirements 9.1, 9.2**
    /// </summary>
    [Fact]
    public void BlockAllForComercio_OperaEnMemoria_SinTocarBd()
    {
        using var arnes = CrearArnes();
        const int comercioId = 7;

        var antesDelBloqueo = DateTime.UtcNow.AddMinutes(-1);

        arnes.Sut.BlockAllForComercio(comercioId);

        var despuesDelBloqueo = DateTime.UtcNow.AddMinutes(1);

        Assert.True(arnes.Sut.IsComercioBlocked(comercioId, antesDelBloqueo));
        Assert.False(arnes.Sut.IsComercioBlocked(comercioId, despuesDelBloqueo));
        Assert.False(arnes.Sut.IsComercioBlocked(123, antesDelBloqueo));
        Assert.Equal(0, arnes.ControlDb.JtiRevocados.Count());
    }
}
