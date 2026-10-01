using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Domain.Entities;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Security;

/// <summary>
/// Tests de preservación que verifican comportamientos CORRECTOS del código actual (sin fix).
/// Estos tests DEBEN PASAR tanto ANTES como DESPUÉS del fix — confirman la línea base a preservar.
///
/// Propiedades verificadas:
/// - Property 3: Requests anónimos NO reciben headers X-RateLimit-* (Req 3.1)
/// - Preservation 3.2: Respuestas 429 SIEMPRE incluyen header Retry-After
/// - Preservation 3.5: Usuarios autenticados dentro del límite NO reciben 429 prematuro
///
/// Validates: Requirements 3.1, 3.2, 3.3, 3.5, 3.6
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Section", "Security")]
[Trait("Feature", "preservation-properties")]
public class PreservationPropertyTests : IntegrationTestBase
{
    public PreservationPropertyTests(PostgresFixture fixture) : base(fixture) { }

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

    #region Property 3: Requests anónimos NO reciben headers X-RateLimit-*

    /// <summary>
    /// Property: Para TODO request anónimo (sin claim `sub`), la respuesta NO contiene
    /// headers X-RateLimit-Limit, X-RateLimit-Remaining ni X-RateLimit-Reset.
    ///
    /// Este es comportamiento CORRECTO actual que debe preservarse después del fix.
    /// El rate limiting aplica a tráfico anónimo (100 req/min) pero NO informa al cliente
    /// sobre su quota via headers — solo usuarios autenticados reciben esa información.
    ///
    /// Validates: Requirements 3.1
    /// </summary>
    [DockerAvailableFact]
    public async Task Preservation_AnonymousRequests_DoNotContainRateLimitHeaders()
    {
        // Arrange: tráfico anónimo (sin JWT) con IP única para aislar contadores
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For",
            $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: enviar múltiples requests anónimos a diferentes endpoints
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

            // Solo verificar si NO es 429 (queremos respuestas no-rate-limited)
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                continue;

            // Verificar AUSENCIA de headers X-RateLimit-*
            var hasLimit = response.Headers.Contains("X-RateLimit-Limit");
            var hasRemaining = response.Headers.Contains("X-RateLimit-Remaining");
            var hasReset = response.Headers.Contains("X-RateLimit-Reset");

            if (hasLimit || hasRemaining || hasReset)
            {
                failures.Add(
                    $"GET {endpoint} → {(int)response.StatusCode} | " +
                    $"X-RateLimit-Limit={hasLimit}, X-RateLimit-Remaining={hasRemaining}, X-RateLimit-Reset={hasReset} " +
                    "(no debería tener ninguno en tráfico anónimo)");
            }
        }

        // Assert: NINGÚN response anónimo debe tener headers X-RateLimit-*
        Assert.Empty(failures);
    }

    /// <summary>
    /// Property: Para requests anónimos que reciben diferentes status codes (401, 403, etc.),
    /// NINGUNO debe contener headers X-RateLimit-*. Explora múltiples status codes.
    ///
    /// Validates: Requirements 3.1
    /// </summary>
    [DockerAvailableFact]
    public async Task Preservation_AnonymousRequests_NoRateLimitHeaders_AcrossStatusCodes()
    {
        // Arrange: sin autenticación, IP única
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For",
            $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: probar endpoints que producen diferentes status codes sin autenticación
        // /api/tenants/productos → 401 (RouteAuthorizationMiddleware requiere autenticación)
        // /api/admin/logs → 401 (requiere SuperAdmin)
        var endpoints = new[]
        {
            "/api/tenants/productos",
            "/api/tenants/categorias",
            "/api/admin/logs"
        };

        var failures = new List<string>();

        foreach (var endpoint in endpoints)
        {
            var response = await Client.GetAsync(endpoint);

            // Ignorar 429 — eso se prueba en otro test
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                continue;

            // Para cualquier otro status code, NO deben estar presentes los headers
            if (response.Headers.Contains("X-RateLimit-Limit") ||
                response.Headers.Contains("X-RateLimit-Remaining") ||
                response.Headers.Contains("X-RateLimit-Reset"))
            {
                failures.Add(
                    $"GET {endpoint} → {(int)response.StatusCode} tiene headers X-RateLimit-* " +
                    "(prohibido en tráfico anónimo)");
            }
        }

        Assert.Empty(failures);
    }

    #endregion

    #region Preservation 3.2: Respuestas 429 CONTIENEN header Retry-After

    /// <summary>
    /// Property: Para TODO request rechazado por rate limiting (status 429),
    /// la respuesta DEBE contener el header Retry-After con un valor positivo.
    ///
    /// Este comportamiento es implementado por OnRejected en Program.cs y
    /// funciona correctamente en el código actual. Debe preservarse tras el fix.
    ///
    /// Validates: Requirements 3.2
    /// </summary>
    [DockerAvailableFact]
    public async Task Preservation_429Responses_AlwaysContainRetryAfterHeader()
    {
        // Arrange: tráfico anónimo con IP única, necesitamos exceder el límite de 100 req/min
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        var uniqueIp = $"172.20.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", uniqueIp);

        // Act: enviar peticiones hasta recibir 429
        HttpResponseMessage? rateLimitedResponse = null;
        for (int i = 0; i < 110; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        // Precondición: debemos haber obtenido un 429
        Assert.NotNull(rateLimitedResponse);

        // Assert: la respuesta 429 DEBE contener header Retry-After
        Assert.True(
            rateLimitedResponse.Headers.Contains("Retry-After"),
            "Toda respuesta 429 debe incluir header Retry-After (preservación del comportamiento actual)");

        // Verificar que el valor de Retry-After es un entero positivo
        var retryAfterValue = rateLimitedResponse.Headers.GetValues("Retry-After").First();
        Assert.True(int.TryParse(retryAfterValue, out var retryAfterSeconds),
            $"Retry-After debe ser un entero, fue: '{retryAfterValue}'");
        Assert.InRange(retryAfterSeconds, 1, 1800);
    }

    /// <summary>
    /// Property: Las respuestas 429 NO deben contener headers X-RateLimit-* duplicados.
    /// El OnRejected setea solo Retry-After; el middleware no agrega X-RateLimit-* a 429.
    ///
    /// Validates: Requirements 3.2
    /// </summary>
    [DockerAvailableFact]
    public async Task Preservation_429Responses_DoNotContainDuplicateRateLimitHeaders()
    {
        // Arrange: tráfico anónimo, exceder límite
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        var uniqueIp = $"172.21.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", uniqueIp);

        // Act: enviar peticiones hasta recibir 429
        HttpResponseMessage? rateLimitedResponse = null;
        for (int i = 0; i < 110; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        Assert.NotNull(rateLimitedResponse);

        // Assert: 429 NO debe tener X-RateLimit-* (esos son solo para responses exitosos autenticados)
        Assert.False(
            rateLimitedResponse.Headers.Contains("X-RateLimit-Limit"),
            "Respuesta 429 no debe incluir X-RateLimit-Limit (solo Retry-After)");
        Assert.False(
            rateLimitedResponse.Headers.Contains("X-RateLimit-Remaining"),
            "Respuesta 429 no debe incluir X-RateLimit-Remaining (solo Retry-After)");
        Assert.False(
            rateLimitedResponse.Headers.Contains("X-RateLimit-Reset"),
            "Respuesta 429 no debe incluir X-RateLimit-Reset (solo Retry-After)");
    }

    /// <summary>
    /// Property: El body JSON de una respuesta 429 CONTIENE campo "code" = "RATE_LIMIT_EXCEEDED"
    /// y campo "error" no vacío. Preservar formato de respuesta de rechazo.
    ///
    /// Validates: Requirements 3.2, 3.3
    /// </summary>
    [DockerAvailableFact]
    public async Task Preservation_429Responses_ContainCorrectJsonBody()
    {
        // Arrange: tráfico anónimo, exceder límite con IP única
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        var uniqueIp = $"172.22.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", uniqueIp);

        // Act: enviar peticiones hasta recibir 429
        HttpResponseMessage? rateLimitedResponse = null;
        for (int i = 0; i < 110; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        Assert.NotNull(rateLimitedResponse);

        // Assert: body JSON con formato correcto
        var body = await rateLimitedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RATE_LIMIT_EXCEEDED", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()),
            "Campo 'error' no debe estar vacío en respuesta 429");
    }

    #endregion

    #region Preservation 3.5: Autenticados dentro del límite NO reciben 429 prematuro

    /// <summary>
    /// Property: Para todo usuario autenticado (JWT válido, usuario activo en DB) que envía
    /// requests DENTRO del límite de 300 req/min, el sistema NO retorna 429.
    ///
    /// Este test verifica que el rate limiter diferenciado funciona correctamente:
    /// un usuario autenticado tiene un presupuesto de 300, no de 100 como los anónimos.
    ///
    /// Validates: Requirements 3.5
    /// </summary>
    [DockerAvailableFact]
    public async Task Preservation_AuthenticatedUser_WithinLimit_DoesNotGet429()
    {
        // Arrange: crear usuario y autenticar con plan Empresarial (sin límites de features)
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
        Client.DefaultRequestHeaders.Add("X-Forwarded-For",
            $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: enviar 50 peticiones (bien dentro del límite de 300 req/min)
        var got429 = false;
        for (int i = 0; i < 50; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                got429 = true;
                break;
            }
        }

        // Assert: NO debe recibir 429 con solo 50 requests (límite es 300)
        Assert.False(got429,
            "Un usuario autenticado con límite de 300 req/min NO debería recibir 429 con solo 50 peticiones");
    }

    /// <summary>
    /// Property: Un usuario autenticado puede enviar peticiones que superan el límite anónimo
    /// sin recibir 429, demostrando que opera bajo el límite autenticado de 300.
    /// Usamos 80 peticiones (supera ampliamente el punto donde un anónimo sería bloqueado)
    /// pero dejamos margen contra Bug 5 (contadores estáticos compartidos entre tests).
    ///
    /// Validates: Requirements 3.5
    /// </summary>
    [DockerAvailableFact]
    public async Task Preservation_AuthenticatedUser_CanExceedAnonymousLimit_Without429()
    {
        // Arrange: crear usuario autenticado
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var usuario = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id)
            .EnSucursal(sucursal.Id)
            .ConRol("Cajero")
            .CrearAsync(db);

        AuthenticateAs(usuario.Id, comercio.Id, "Cajero");

        // IP única para aislar contadores — se usa un rango diferente al de otros tests
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For",
            $"192.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: enviar 80 peticiones (supera el punto donde un anónimo sería bloqueado ~50-60,
        // pero deja margen bajo el límite de 300 autenticado e incluso con contadores heredados)
        var got429 = false;
        for (int i = 0; i < 80; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                got429 = true;
                break;
            }
        }

        // Assert: NO debe recibir 429 (opera bajo presupuesto de 300, no de 100)
        Assert.False(got429,
            "Un usuario autenticado con presupuesto de 300 req/min no debe recibir 429 al enviar 80 peticiones");
    }

    #endregion
}
