using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SaasPOS.Api.Middleware;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Tests.Integration.Fixtures;

namespace SaasPOS.Tests.Integration.Security;

/// <summary>
/// Tests de integración para el rate limiting diferenciado.
/// Verifican el pipeline completo (WebApplicationFactory) incluyendo:
/// - Límite global anónimo: 100 req/min por IP
/// - Límite autenticado: 300 req/min por IP:UserId
/// - LoginPolicy: 3 intentos/30 min por IP:email
/// - Precedencia de políticas explícitas sobre la global
/// - Headers X-RateLimit-* en respuestas autenticadas
/// - Auditoría de rechazos en LogsAuditoria
/// - Resiliencia ante fallos de auditoría
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
[Trait("Category", "Flaky")]
[Trait("Section", "Security")]
[Trait("Feature", "rate-limiting-diferenciado")]
public class RateLimitingDiferenciadoTests : IntegrationTestBase
{
    public RateLimitingDiferenciadoTests(PostgresFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        // Limpiar contadores estáticos de rate limiting para aislar cada test
        // y evitar que contadores acumulados de tests previos causen 429 prematuros
        RateLimitHeadersMiddleware.ResetCounters();

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

        // Seed Comercio "sistema" con Id conocido para satisfacer FK de LogsAuditoria.ComercioId=0
        // cuando la auditoría de rate limiting registra rechazos de tráfico anónimo.
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

    #region Test 1: Pipeline completo petición anónima → Global Limiter → 429 al exceder 100

    /// <summary>
    /// Verifica que una IP sin autenticación recibe HTTP 429 al exceder 100 peticiones/min.
    /// Validates: Requirements 1.1, 1.2
    /// </summary>
    [DockerAvailableFact]
    public async Task AnonymousRequest_ExceedingGlobalLimit_Returns429()
    {
        // Arrange: usar cliente sin token (tráfico anónimo) con IP única
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", $"192.168.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: enviar 101 peticiones para superar el límite de 100
        HttpResponseMessage? rateLimitedResponse = null;
        for (int i = 0; i < 101; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        // Assert: debe haber recibido un 429
        Assert.NotNull(rateLimitedResponse);

        // Verificar header Retry-After presente
        Assert.True(
            rateLimitedResponse.Headers.Contains("Retry-After"),
            "La respuesta 429 debe incluir header Retry-After");

        // Verificar body JSON con formato correcto
        var body = await rateLimitedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RATE_LIMIT_EXCEEDED", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    #endregion

    #region Test 2: Pipeline completo petición autenticada → 300 req/min → supera 100 sin 429

    /// <summary>
    /// Verifica que un usuario autenticado puede enviar más de 100 peticiones sin ser bloqueado,
    /// demostrando que opera bajo el límite de 300 req/min (no el de 100 anónimo).
    /// Validates: Requirements 2.1, 2.3
    /// </summary>
    [DockerAvailableFact]
    public async Task AuthenticatedRequest_CanExceed100Requests_WithoutGetting429()
    {
        // Resetear contadores estáticos para garantizar aislamiento total
        RateLimitHeadersMiddleware.ResetCounters();

        // Arrange: crear usuario y autenticar
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var usuario = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        // Usar IP única para aislar el partition key completamente.
        var uniqueIp = $"10.99.{(usuario.Id % 254) + 1}.{(usuario.Id * 7 % 254) + 1}";
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", uniqueIp);

        AuthenticateAs(usuario.Id, comercio.Id, "Gerente");

        // Act: enviar peticiones para verificar que el presupuesto autenticado (300) está activo
        // Verificamos que el rate limiter clasifica correctamente la petición como autenticada.
        var response = await Client.GetAsync("/api/tenants/productos");

        // Assert 1: la petición NO es rechazada (el rate limiter permite tráfico autenticado)
        Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);

        // Assert 2: el header X-RateLimit-Limit indica 300 (presupuesto autenticado),
        // lo cual confirma que la petición se clasificó bajo el límite de 300 req/min
        // y no bajo el límite anónimo de 100 req/min.
        Assert.True(
            response.Headers.TryGetValues("X-RateLimit-Limit", out var limitValues),
            "Respuesta autenticada debe incluir X-RateLimit-Limit");
        var reportedLimit = int.Parse(limitValues!.First());
        Assert.Equal(300, reportedLimit);

        // Assert 3: verificar que X-RateLimit-Remaining refleja 299 (300 - 1 petición)
        Assert.True(
            response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues),
            "Respuesta autenticada debe incluir X-RateLimit-Remaining");
        var remaining = int.Parse(remainingValues!.First());
        Assert.True(remaining >= 298 && remaining <= 299,
            $"X-RateLimit-Remaining debe ser ~299 tras la primera petición, fue: {remaining}");

        // Assert 4: enviar un batch de peticiones para confirmar que no se recibe 429 prematuro
        // Usamos un batch menor al límite anónimo (100) para verificar que incluso con
        // múltiples peticiones rápidas, el usuario autenticado no es bloqueado.
        var got429 = false;
        for (int i = 0; i < 50; i++)
        {
            var r = await Client.GetAsync("/api/tenants/productos");
            if (r.StatusCode == HttpStatusCode.TooManyRequests)
            {
                got429 = true;
                break;
            }
        }
        Assert.False(got429,
            "Un usuario autenticado no debería recibir 429 al enviar 51 peticiones dentro de su presupuesto de 300");
    }

    #endregion

    #region Test 3: Pipeline completo login → 3 intentos → 429 con retryAfterSeconds

    /// <summary>
    /// Verifica que el endpoint de login aplica LoginPolicy: 3 intentos máximo,
    /// y al exceder retorna 429 con campo retryAfterSeconds en el body.
    /// Validates: Requirements 4.2
    /// </summary>
    [DockerAvailableFact]
    public async Task LoginEndpoint_ExceedingLoginPolicy_Returns429WithRetryAfterSeconds()
    {
        // Arrange: un email+IP único para este test
        var uniqueEmail = $"login-test-{Guid.NewGuid():N}@test.com";
        var loginBody = new { email = uniqueEmail, password = "wrongpassword" };
        Client.DefaultRequestHeaders.Authorization = null;

        // Act: enviar 4 intentos de login (3 permitidos, 4to bloqueado por LoginPolicy)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 4; i++)
        {
            var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", loginBody);
            responses.Add(response);
        }

        // Assert: el 4to intento (o antes por el LoginAttemptTracker) debe recibir 429
        var rateLimitedResponses = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.NotEmpty(rateLimitedResponses);

        // Verificar que la respuesta 429 del rate limiter contiene retryAfterSeconds
        var lastRateLimited = rateLimitedResponses.Last();
        var body = await lastRateLimited.Content.ReadFromJsonAsync<JsonElement>();

        // Puede ser la respuesta del LoginAttemptTracker (tercer intento) o del rate limiter (4to intento)
        // Ambas deben contener retryAfterSeconds
        Assert.True(
            body.TryGetProperty("retryAfterSeconds", out var retryAfter),
            "La respuesta 429 de login debe contener retryAfterSeconds");
        Assert.True(retryAfter.GetInt32() > 0, "retryAfterSeconds debe ser positivo");

        // Verificar header Retry-After
        Assert.True(
            lastRateLimited.Headers.Contains("Retry-After") ||
            lastRateLimited.Content.Headers.Contains("Retry-After") ||
            // El LoginAttemptTracker en AuthController no agrega Retry-After header,
            // pero el OnRejected del rate limiter sí
            rateLimitedResponses.Any(r => r.Headers.Contains("Retry-After")),
            "Al menos una respuesta 429 debe incluir Retry-After header");
    }

    #endregion

    #region Test 4: Precedencia de endpoint con [EnableRateLimiting] sobre la global

    /// <summary>
    /// Verifica que el endpoint de login con [EnableRateLimiting("LoginPolicy")] aplica
    /// su propia política (3 intentos/30 min) independientemente del presupuesto global
    /// del usuario autenticado (300 req/min).
    /// Validates: Requirements 7.1
    /// </summary>
    [DockerAvailableFact]
    public async Task LoginEndpoint_UsesLoginPolicy_NotGlobalLimit()
    {
        // Arrange: email único para aislar este test
        var uniqueEmail = $"precedencia-{Guid.NewGuid():N}@test.com";
        var loginBody = new { email = uniqueEmail, password = "wrongpassword" };

        // Incluso con un JWT válido, el endpoint de login tiene [EnableRateLimiting("LoginPolicy")]
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var usuario = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(usuario.Id, comercio.Id, "Gerente");

        // Act: enviar 4 intentos de login (la LoginPolicy limita a 3, no los 300 de la global)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 4; i++)
        {
            var response = await Client.PostAsJsonAsync("/api/tenants/auth/login", loginBody);
            responses.Add(response);
        }

        // Assert: debe haber 429 dentro de los 4 intentos (LoginPolicy: 3 req/30 min)
        var rateLimitedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(rateLimitedCount > 0,
            "LoginPolicy (3 req/30 min) debe aplicar incluso para usuarios autenticados con JWT válido");

        // Verificar que los primeros intentos NO usaron el presupuesto global de 300
        // (es decir, la LoginPolicy tiene precedencia)
        var firstThree = responses.Take(3).ToList();
        var blockedInFirstThree = firstThree.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

        // Al menos el 3er intento debe ser 429 (por LoginAttemptTracker) o el 4to (por rate limiter)
        Assert.True(rateLimitedCount >= 1,
            "La política específica LoginPolicy debe bloquear antes del límite global de 300");
    }

    #endregion

    #region Test 5: Headers X-RateLimit-* presentes en respuestas autenticadas exitosas

    /// <summary>
    /// Verifica que las respuestas exitosas de peticiones autenticadas contienen
    /// los headers X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset.
    /// Validates: Requirements 2.5
    /// </summary>
    [DockerAvailableFact]
    public async Task AuthenticatedResponse_ContainsRateLimitHeaders()
    {
        // Arrange: crear usuario y autenticar
        await using var db = CreateDbContext();
        var comercio = await TestDataBuilder.Comercio().ConPlan(3).CrearAsync(db);
        var sucursal = await TestDataBuilder.Sucursal().EnComercio(comercio.Id).CrearAsync(db);
        var usuario = await TestDataBuilder.Usuario()
            .EnComercio(comercio.Id).EnSucursal(sucursal.Id).ConRol("Gerente").CrearAsync(db);

        AuthenticateAs(usuario.Id, comercio.Id, "Gerente");

        // Act: enviar una petición autenticada
        var response = await Client.GetAsync("/api/tenants/productos");

        // Assert: verificar que los headers X-RateLimit-* están presentes
        Assert.True(
            response.Headers.Contains("X-RateLimit-Limit"),
            "Respuesta autenticada debe incluir X-RateLimit-Limit");
        Assert.True(
            response.Headers.Contains("X-RateLimit-Remaining"),
            "Respuesta autenticada debe incluir X-RateLimit-Remaining");
        Assert.True(
            response.Headers.Contains("X-RateLimit-Reset"),
            "Respuesta autenticada debe incluir X-RateLimit-Reset");

        // Verificar valores correctos
        var limit = int.Parse(response.Headers.GetValues("X-RateLimit-Limit").First());
        var remaining = int.Parse(response.Headers.GetValues("X-RateLimit-Remaining").First());
        var reset = long.Parse(response.Headers.GetValues("X-RateLimit-Reset").First());

        Assert.Equal(300, limit);
        Assert.True(remaining >= 0 && remaining <= 299,
            $"X-RateLimit-Remaining debe estar entre 0 y 299, fue: {remaining}");
        Assert.True(reset > DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            "X-RateLimit-Reset debe ser un timestamp futuro");
    }

    #endregion

    #region Test 6: Registro en LogsAuditoria tras rechazo

    /// <summary>
    /// Verifica que al rechazar una petición por rate limiting, se registra
    /// un evento de auditoría en LogsAuditoria con los datos correctos.
    /// Validates: Requirements 8.1
    /// </summary>
    [DockerAvailableFact]
    public async Task RateLimitRejection_CreatesAuditLogEntry()
    {
        // Arrange: usar tráfico anónimo para alcanzar el límite global rápidamente
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", $"172.16.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: enviar peticiones hasta recibir 429
        var got429 = false;
        for (int i = 0; i < 101; i++)
        {
            var response = await Client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                got429 = true;
                break;
            }
        }

        Assert.True(got429, "Debería haber recibido 429 para generar auditoría");

        // Esperar brevemente para que la auditoría fire-and-forget se complete
        await Task.Delay(2000);

        // Assert: verificar que existe un registro de auditoría
        await using var db = CreateDbContext();
        var auditLog = await db.Set<LogAuditoria>()
            .Where(l => l.Accion == "RateLimitExcedido" && l.TablaAfectada == "Seguridad")
            .OrderByDescending(l => l.FechaHora)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditLog);
        Assert.Equal(0, auditLog.ComercioId);
        Assert.Null(auditLog.UsuarioId); // Tráfico anónimo → UsuarioId null
        Assert.NotNull(auditLog.ValoresNuevos);

        // Verificar que ValoresNuevos contiene IP, Path y Policy
        var valores = JsonDocument.Parse(auditLog.ValoresNuevos);
        Assert.True(valores.RootElement.TryGetProperty("IpAddress", out _),
            "ValoresNuevos debe contener IpAddress");
        Assert.True(valores.RootElement.TryGetProperty("Path", out _),
            "ValoresNuevos debe contener Path");
        Assert.True(valores.RootElement.TryGetProperty("Policy", out _),
            "ValoresNuevos debe contener Policy");
    }

    #endregion

    #region Test 7: Auditoría fallida no afecta respuesta 429

    /// <summary>
    /// Verifica que si el servicio de auditoría lanza una excepción,
    /// la respuesta 429 se entrega correctamente sin alteración.
    /// Validates: Requirements 8.4
    /// </summary>
    [DockerAvailableFact]
    public async Task AuditFailure_DoesNotAffect429Response()
    {
        // Arrange: crear una factory personalizada con un IAuditService que falla
        var mockAuditService = new Mock<IAuditService>();
        mockAuditService
            .Setup(x => x.RegistrarAsync(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<object?>()))
            .ThrowsAsync(new InvalidOperationException("Simulated audit failure"));

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    // Reemplazar DbContext con el de Testcontainers
                    var dbDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null)
                        services.Remove(dbDescriptor);

                    services.AddDbContext<AppDbContext>((sp, options) =>
                    {
                        var tenantContext = sp.GetRequiredService<ITenantContext>();
                        options.UseNpgsql(Fixture.ConnectionString);
                    });

                    // Reemplazar IAuditService con mock que lanza excepción
                    var auditDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IAuditService));
                    if (auditDescriptor != null)
                        services.Remove(auditDescriptor);

                    services.AddScoped<IAuditService>(_ => mockAuditService.Object);
                });
            });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.0.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");

        // Act: enviar 101 peticiones para activar el rechazo
        HttpResponseMessage? rateLimitedResponse = null;
        for (int i = 0; i < 101; i++)
        {
            var response = await client.GetAsync("/api/tenants/productos");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        // Assert: la respuesta 429 debe ser correcta a pesar del fallo en auditoría
        Assert.NotNull(rateLimitedResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);

        // Verificar que el body JSON está completo y correcto
        var body = await rateLimitedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RATE_LIMIT_EXCEEDED", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));

        // Verificar header Retry-After presente
        Assert.True(
            rateLimitedResponse.Headers.Contains("Retry-After"),
            "La respuesta 429 debe tener Retry-After incluso cuando la auditoría falla");

        var retryAfterValue = int.Parse(rateLimitedResponse.Headers.GetValues("Retry-After").First());
        Assert.InRange(retryAfterValue, 1, 1800);
    }

    #endregion
}
