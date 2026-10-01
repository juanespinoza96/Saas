using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Security;

/// <summary>
/// Tests de exploración de bug conditions para confirmar que los bugs de producción existen.
/// Estos tests DEBEN FALLAR en código sin fix — el fallo confirma la existencia del bug.
///
/// Bug 1: Headers X-RateLimit-* perdidos porque RateLimitHeadersMiddleware escribe headers
///         DESPUÉS de await _next(context), cuando Response.HasStarted == true.
/// Bug 2: Auditoría perdida porque RegistrarAuditoriaRechazoAsync resuelve IServiceScopeFactory
///         desde RequestServices (scope del request) dentro de Task.Run, causando ObjectDisposedException.
///
/// Validates: Requirements 1.1, 1.2, 2.1, 2.2
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Security")]
[Trait("Feature", "bug-condition-exploration")]
public class BugConditionExplorationTests : IntegrationTestBase
{
    public BugConditionExplorationTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanDatabaseAsync();
        await SeedPlansAsync();
    }

    private async Task SeedPlansAsync()
    {
        await using var db = CreateDbContext();
        if (!await db.Set<Plan>().AnyAsync())
        {
            db.Set<Plan>().AddRange(
                new Plan { Id = 1, Nombre = "Básico", Precio = 350m, LimiteUsuarios = 2, LimiteAtributos = 2, LimiteSucursales = 1 },
                new Plan { Id = 2, Nombre = "Intermedio", Precio = 750m, LimiteUsuarios = 3, LimiteAtributos = 5, LimiteSucursales = 0 },
                new Plan { Id = 3, Nombre = "Empresarial", Precio = 1200m, LimiteUsuarios = 0, LimiteAtributos = 0, LimiteSucursales = 0 }
            );
            await db.SaveChangesAsync();
        }

        // Seed Comercio "sistema" para FK de LogsAuditoria.ComercioId=0
        if (!await db.Comercios.IgnoreQueryFilters().AnyAsync(c => c.Ruc == "0000000000000"))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Comercios"" (""Id"", ""Ruc"", ""RazonSocial"", ""PlanId"", ""UsaFacturacionSRI"", ""Estado"", ""FechaRegistro"")
                  VALUES (0, '0000000000000', 'Sistema', 1, false, 'Activo', NOW())
                  ON CONFLICT (""Id"") DO NOTHING");
            await db.Database.ExecuteSqlRawAsync(
                @"SELECT setval(pg_get_serial_sequence('""Comercios""', 'Id'), GREATEST((SELECT MAX(""Id"") FROM ""Comercios""), 1))");
        }
    }

    #region Bug 1: Headers X-RateLimit-* perdidos en respuestas autenticadas

    /// <summary>
    /// Property: Para TODO request autenticado (JWT válido, claim sub) que NO resulta en 429,
    /// la respuesta DEBE contener headers X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset.
    ///
    /// Este test genera múltiples endpoints y verifica que los headers están presentes.
    /// En código buggy, el middleware escribe headers DESPUÉS de _next(), cuando HasStarted=true,
    /// por lo que los headers NUNCA se escriben → el test FALLA.
    ///
    /// Validates: Requirements 2.1, 2.5
    /// </summary>
    [DockerAvailableFact]
    public async Task Bug1_AuthenticatedRequests_MustContainRateLimitHeaders()
    {
        // Arrange: crear usuario autenticado con plan Empresarial (sin límites de features)
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var usuario = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id)
            .EnSucursal(sucursal.Id)
            .ConRol("Gerente")
            .CrearAsync(db);

        AuthenticateAs(usuario.Id, comercio.Id, "Gerente");

        // IP única para aislar contadores de rate limiting
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: Generar requests a múltiples endpoints autenticados para explorar el bug condition
        var endpoints = new[]
        {
            "/api/tenants/productos",
            "/api/tenants/categorias",
            "/api/tenants/clientes"
        };

        var failures = new List<string>();

        foreach (var endpoint in endpoints)
        {
            var response = await Client.GetAsync(endpoint);

            // Solo verificar si el response NO es 429 (queremos respuestas exitosas o 404, no rate limited)
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                continue;

            // Verificar presencia de headers X-RateLimit-* — esto FALLA en código buggy
            var hasLimit = response.Headers.Contains("X-RateLimit-Limit");
            var hasRemaining = response.Headers.Contains("X-RateLimit-Remaining");
            var hasReset = response.Headers.Contains("X-RateLimit-Reset");

            if (!hasLimit || !hasRemaining || !hasReset)
            {
                failures.Add(
                    $"GET {endpoint} → {(int)response.StatusCode} | " +
                    $"X-RateLimit-Limit={hasLimit}, X-RateLimit-Remaining={hasRemaining}, X-RateLimit-Reset={hasReset}");
            }
        }

        // Assert: TODOS los responses autenticados deben tener headers X-RateLimit-*
        // En código buggy esto FALLA porque Response.HasStarted=true impide escritura de headers
        Assert.Empty(failures);
    }

    /// <summary>
    /// Property parameterizada: Para diferentes roles autenticados, los headers X-RateLimit-*
    /// deben estar presentes en la respuesta. Explora múltiples combinaciones de rol + endpoint.
    ///
    /// Validates: Requirements 2.1
    /// </summary>
    [DockerAvailableFact]
    public async Task Bug1_MultipleRoles_AllMustReceiveRateLimitHeaders()
    {
        // Arrange: crear comercio con sucursal
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);

        // Generar usuarios con diferentes roles
        var roles = new[] { "Gerente", "Cajero", "Bodeguero" };
        var failures = new List<string>();

        foreach (var role in roles)
        {
            var usuario = await TestDataBuilder.Usuario()
                .EnComercio(comercio.Id)
                .EnSucursal(sucursal.Id)
                .ConRol(role)
                .CrearAsync(db);

            AuthenticateAs(usuario.Id, comercio.Id, role);

            // IP única por rol para aislar contadores
            Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            Client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

            var response = await Client.GetAsync("/api/tenants/productos");

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                continue;

            var hasLimit = response.Headers.Contains("X-RateLimit-Limit");
            var hasRemaining = response.Headers.Contains("X-RateLimit-Remaining");
            var hasReset = response.Headers.Contains("X-RateLimit-Reset");

            if (!hasLimit || !hasRemaining || !hasReset)
            {
                failures.Add(
                    $"Role={role}, UserId={usuario.Id} → {(int)response.StatusCode} | " +
                    $"Limit={hasLimit}, Remaining={hasRemaining}, Reset={hasReset}");
            }
        }

        // Assert: todos los roles deben recibir headers X-RateLimit-*
        Assert.Empty(failures);
    }

    /// <summary>
    /// Property: Cuando los headers X-RateLimit-* están presentes, sus valores deben ser
    /// semánticamente correctos: Limit=300, Remaining en [0,299], Reset es timestamp futuro.
    ///
    /// Validates: Requirements 2.1, 2.5
    /// </summary>
    [DockerAvailableFact]
    public async Task Bug1_RateLimitHeaders_WhenPresent_MustHaveCorrectValues()
    {
        // Arrange
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var usuario = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id)
            .EnSucursal(sucursal.Id)
            .ConRol("Gerente")
            .CrearAsync(db);

        AuthenticateAs(usuario.Id, comercio.Id, "Gerente");

        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: enviar request autenticado
        var response = await Client.GetAsync("/api/tenants/productos");

        // Skip si es 429 (no es lo que probamos aquí)
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return;

        // Assert: los headers DEBEN estar presentes (falla en código buggy)
        Assert.True(
            response.Headers.Contains("X-RateLimit-Limit"),
            $"Request autenticado GET /api/tenants/productos retorna {(int)response.StatusCode} " +
            "pero NO incluye header X-RateLimit-Limit. Bug 1 confirmado: " +
            "RateLimitHeadersMiddleware no puede escribir headers después de Response.HasStarted");

        // Si están presentes, verificar valores correctos
        if (response.Headers.Contains("X-RateLimit-Limit"))
        {
            var limit = int.Parse(response.Headers.GetValues("X-RateLimit-Limit").First());
            var remaining = int.Parse(response.Headers.GetValues("X-RateLimit-Remaining").First());
            var reset = long.Parse(response.Headers.GetValues("X-RateLimit-Reset").First());

            Assert.Equal(300, limit);
            Assert.InRange(remaining, 0, 299);
            Assert.True(reset > DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                "X-RateLimit-Reset debe ser un timestamp futuro");
        }
    }

    #endregion

    #region Bug 2: Auditoría perdida por scope disposal en Task.Run

    /// <summary>
    /// Property: Para TODO request rechazado por rate limiting (429), DEBE existir un registro
    /// en LogsAuditoria con Accion="RateLimitExcedido" dentro de un periodo razonable.
    ///
    /// En código buggy, el Task.Run resuelve IServiceScopeFactory desde RequestServices
    /// (scope per-request). Si el request scope se dispone antes de que Task.Run ejecute,
    /// se lanza ObjectDisposedException que es silenciada por catch{}, perdiendo la auditoría.
    ///
    /// Validates: Requirements 2.2
    /// </summary>
    [DockerAvailableFact]
    public async Task Bug2_RateLimitRejection_MustCreateAuditLogEntry()
    {
        // Arrange: usar tráfico anónimo con IP única para alcanzar límite rápido
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        var uniqueIp = $"172.16.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", uniqueIp);

        // Limpiar logs de auditoría previos
        await using (var dbClean = CreateDbContext())
        {
            await dbClean.Database.ExecuteSqlRawAsync(
                @"DELETE FROM ""LogsAuditoria"" WHERE ""Accion"" = 'RateLimitExcedido'");
        }

        // Act: enviar peticiones hasta recibir 429 (exceder límite anónimo de 100 req/min)
        var got429 = false;
        for (int i = 0; i < 110; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                got429 = true;
                break;
            }
        }

        // Precondición: debemos haber obtenido un 429
        Assert.True(got429, "Debe recibir 429 para generar auditoría. Si no se alcanza, el rate limiter no está funcionando.");

        // Esperar para que el fire-and-forget de auditoría complete (o falle por scope disposal)
        await Task.Delay(3000);

        // Assert: verificar que la auditoría se registró
        // En código buggy, el ObjectDisposedException causa que el registro se pierda
        await using var db = CreateDbContext();
        var auditLog = await db.Set<LogAuditoria>()
            .Where(l => l.Accion == "RateLimitExcedido" && l.TablaAfectada == "Seguridad")
            .OrderByDescending(l => l.FechaHora)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditLog);
    }

    /// <summary>
    /// Property: El registro de auditoría de un rechazo 429 DEBE contener datos completos:
    /// IpAddress, Path, y Policy en el campo ValoresNuevos (JSONB).
    ///
    /// Validates: Requirements 2.2
    /// </summary>
    [DockerAvailableFact]
    public async Task Bug2_AuditLogEntry_MustContainCompleteData()
    {
        // Arrange: IP única para este test
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        var uniqueIp = $"192.168.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", uniqueIp);

        // Limpiar logs de auditoría previos
        await using (var dbClean = CreateDbContext())
        {
            await dbClean.Database.ExecuteSqlRawAsync(
                @"DELETE FROM ""LogsAuditoria"" WHERE ""Accion"" = 'RateLimitExcedido'");
        }

        // Act: enviar peticiones hasta recibir 429
        var got429 = false;
        for (int i = 0; i < 110; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                got429 = true;
                break;
            }
        }

        Assert.True(got429, "Debe recibir 429 para generar auditoría");

        // Esperar para que el fire-and-forget complete
        await Task.Delay(3000);

        // Assert: verificar contenido completo del registro de auditoría
        await using var db = CreateDbContext();
        var auditLog = await db.Set<LogAuditoria>()
            .Where(l => l.Accion == "RateLimitExcedido" && l.TablaAfectada == "Seguridad")
            .OrderByDescending(l => l.FechaHora)
            .FirstOrDefaultAsync();

        // En código buggy, auditLog será null porque el scope disposal impide el registro
        Assert.NotNull(auditLog);

        // Verificar datos completos si el registro existe
        Assert.Equal(0, auditLog!.ComercioId);
        Assert.NotNull(auditLog.ValoresNuevos);

        var valores = System.Text.Json.JsonDocument.Parse(auditLog.ValoresNuevos);
        Assert.True(valores.RootElement.TryGetProperty("IpAddress", out _),
            "ValoresNuevos debe contener IpAddress");
        Assert.True(valores.RootElement.TryGetProperty("Path", out _),
            "ValoresNuevos debe contener Path");
        Assert.True(valores.RootElement.TryGetProperty("Policy", out _),
            "ValoresNuevos debe contener Policy");
    }

    #endregion
}
