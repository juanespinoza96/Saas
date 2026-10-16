using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Tests de integración (Testcontainers.PostgreSql) para la persistencia de la
/// revocación individual de JTI y su limpieza periódica:
///
///   1. Persistencia tras reinicio del proceso (Req 8.7, 8.6): una revocación hecha
///      con una instancia de JtiBlocklist debe seguir vigente para una instancia NUEVA
///      con caché VACÍA que apunta a la MISMA base de datos (simula un reinicio: la
///      respuesta solo puede venir de la BD). También valida la auto-expiración lógica
///      (Req 8.6): un jti cuya Expiracion ya pasó se reporta NO bloqueado aunque la fila
///      siga en la tabla.
///
///   2. Limpieza periódica (Req 10.2, 10.4): el barrido borra SOLO las filas vencidas y
///      deja intactas las vigentes, sin alterar el resultado de IsBlocked de estas.
///
/// Se reutiliza el PostgresFixture compartido (contenedor único por colección). Como
/// JtiRevocados es una tabla GLOBAL sin filtro de tenant y el contenedor se comparte
/// entre tests, cada caso usa jti únicos (Guid) para aislarse del estado de otros tests.
/// Todas las marcas de tiempo son UTC.
/// </summary>
[Collection("Postgres")]
public class JtiRevocadoPersistenceTests
{
    private readonly PostgresFixture _fixture;

    public JtiRevocadoPersistenceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Construye un ServiceProvider que registra AppDbContext (Scoped) apuntando al
    /// contenedor del fixture, replicando las opciones de PostgresFixture.CreateDbContext
    /// (EnableRetryOnFailure + CommandTimeout(60)) y un ITenantContext SuperAdmin (sin
    /// query filters). De este proveedor se obtiene el IServiceScopeFactory que JtiBlocklist
    /// y el barrido necesitan para abrir scopes efímeros con un AppDbContext aislado.
    /// </summary>
    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // ITenantContext SuperAdmin: la tabla JtiRevocados no tiene filtro de tenant,
        // pero el AppDbContext exige un ITenantContext para construirse.
        services.AddScoped<ITenantContext>(_ => new SuperAdminTenantContext());

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(_fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
                npgsqlOptions.CommandTimeout(60);
            });
        }, ServiceLifetime.Scoped);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Crea una instancia de JtiBlocklist con una caché en memoria NUEVA y vacía,
    /// usando el IServiceScopeFactory del proveedor dado (misma BD del contenedor).
    /// Construir dos instancias con cachés distintas sobre el mismo proveedor simula
    /// el "reinicio del proceso": la segunda arranca sin estado en memoria.
    /// </summary>
    private static JtiBlocklist CreateBlocklist(IServiceProvider provider)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return new JtiBlocklist(cache, scopeFactory, NullLogger<JtiBlocklist>.Instance);
    }

    [DockerAvailableFact]
    public async Task RevocacionSobreviveReinicio_YRespetaAutoExpiracionLogica()
    {
        await using var provider = BuildServiceProvider();

        // --- Caso A: la revocación sobrevive al "reinicio" (Req 8.7) ---
        var jtiVigente = Guid.NewGuid().ToString();
        var expiracionFutura = DateTime.UtcNow.AddMinutes(30);

        // Instancia #1: revoca el jti (persiste en BD + puebla su caché).
        var blocklistAntes = CreateBlocklist(provider);
        blocklistAntes.AddToBlocklist(jtiVigente, expiracionFutura);

        // Instancia #2: simula reinicio → caché NUEVA y vacía, misma BD.
        // Como la caché está vacía, un IsBlocked == true solo puede venir de la BD,
        // lo que confirma que la revocación sobrevivió al reinicio del proceso.
        var blocklistDespues = CreateBlocklist(provider);
        Assert.True(blocklistDespues.IsBlocked(jtiVigente));

        // --- Caso B: auto-expiración lógica (Req 8.6) ---
        // AddToBlocklist NO persiste tokens ya expirados (Req 8.3), así que para probar
        // la auto-expiración insertamos la fila directamente en la BD con Expiracion en
        // el pasado y confirmamos que IsBlocked devuelve false aunque la fila exista.
        var jtiExpirado = Guid.NewGuid().ToString();
        await using (var context = _fixture.CreateDbContext())
        {
            context.JtiRevocados.Add(new JtiRevocado
            {
                Jti = jtiExpirado,
                Expiracion = DateTime.UtcNow.AddMinutes(-5),
                FechaRevocacion = DateTime.UtcNow.AddMinutes(-10)
            });
            await context.SaveChangesAsync();
        }

        // Caché nueva y vacía: la respuesta viene de la BD, y la fila existe pero está
        // vencida → se trata como NO bloqueado (auto-expiración lógica).
        var blocklistParaExpirado = CreateBlocklist(provider);
        Assert.False(blocklistParaExpirado.IsBlocked(jtiExpirado));

        // La fila sigue en la tabla hasta la limpieza periódica (el "false" es lógico,
        // no por ausencia de fila).
        await using (var verificacion = _fixture.CreateDbContext())
        {
            var fila = await verificacion.JtiRevocados.FirstOrDefaultAsync(j => j.Jti == jtiExpirado);
            Assert.NotNull(fila);
        }
    }

    [DockerAvailableFact]
    public async Task BarridoLimpieza_BorraSoloVencidas_YNoAlteraVigentes()
    {
        await using var provider = BuildServiceProvider();

        // Mezcla de filas: dos vencidas y dos vigentes, con jti únicos para aislarse.
        var vencido1 = Guid.NewGuid().ToString();
        var vencido2 = Guid.NewGuid().ToString();
        var vigente1 = Guid.NewGuid().ToString();
        var vigente2 = Guid.NewGuid().ToString();

        var ahoraAntes = DateTime.UtcNow;

        await using (var context = _fixture.CreateDbContext())
        {
            context.JtiRevocados.AddRange(
                new JtiRevocado { Jti = vencido1, Expiracion = ahoraAntes.AddMinutes(-10), FechaRevocacion = ahoraAntes.AddMinutes(-20) },
                new JtiRevocado { Jti = vencido2, Expiracion = ahoraAntes.AddMinutes(-1), FechaRevocacion = ahoraAntes.AddMinutes(-15) },
                new JtiRevocado { Jti = vigente1, Expiracion = ahoraAntes.AddMinutes(30), FechaRevocacion = ahoraAntes },
                new JtiRevocado { Jti = vigente2, Expiracion = ahoraAntes.AddHours(2), FechaRevocacion = ahoraAntes });
            await context.SaveChangesAsync();
        }

        // Estado de bloqueo de las vigentes ANTES del barrido (caché vacía → desde BD).
        var blocklistAntes = CreateBlocklist(provider);
        Assert.True(blocklistAntes.IsBlocked(vigente1));
        Assert.True(blocklistAntes.IsBlocked(vigente2));

        // Ejecutar UN ciclo del barrido. El método de borrado real del
        // JtiRevocadoCleanupBackgroundService es privado y su bucle corre cada 30 min
        // indefinidamente; por determinismo replicamos aquí EXACTAMENTE el mismo DELETE
        // (db.JtiRevocados.Where(j => j.Expiracion < ahora).ExecuteDeleteAsync(...)) contra
        // el AppDbContext, sin modificar la visibilidad del método de producción.
        int filasBorradas;
        var ahoraBarrido = DateTime.UtcNow;
        await using (var context = _fixture.CreateDbContext())
        {
            filasBorradas = await context.JtiRevocados
                .Where(j => j.Expiracion < ahoraBarrido)
                .ExecuteDeleteAsync();
        }

        // Se borraron al menos nuestras dos filas vencidas (otras filas vencidas de tests
        // previos que compartan el contenedor también podrían contarse; por eso >= 2).
        Assert.True(filasBorradas >= 2);

        // Las filas vencidas ya no están en la tabla.
        await using (var verificacion = _fixture.CreateDbContext())
        {
            Assert.False(await verificacion.JtiRevocados.AnyAsync(j => j.Jti == vencido1));
            Assert.False(await verificacion.JtiRevocados.AnyAsync(j => j.Jti == vencido2));

            // Las vigentes permanecen intactas tras la limpieza.
            Assert.True(await verificacion.JtiRevocados.AnyAsync(j => j.Jti == vigente1));
            Assert.True(await verificacion.JtiRevocados.AnyAsync(j => j.Jti == vigente2));
        }

        // IsBlocked de las vigentes NO cambia tras la limpieza (siguen true). Caché nueva
        // y vacía para forzar que la respuesta venga de la BD post-barrido.
        var blocklistDespues = CreateBlocklist(provider);
        Assert.True(blocklistDespues.IsBlocked(vigente1));
        Assert.True(blocklistDespues.IsBlocked(vigente2));
    }
}
