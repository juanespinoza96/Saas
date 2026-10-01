using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Base class for integration tests using WebApplicationFactory with Testcontainers PostgreSQL.
/// Provides authenticated HTTP client helpers and database seeding utilities.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly PostgresFixture Fixture;
    private WebApplicationFactory<Program> _factory = null!;
    protected HttpClient Client = null!;

    private const string JwtSecretKey = "IntegrationTestSecretKeyThatIsAtLeast32Characters!";
    private const string JwtIssuer = "saas-pos-api";
    private const string JwtAudience = "saas-pos-clients";

    protected IntegrationTestBase(PostgresFixture fixture)
    {
        Fixture = fixture;
    }

    public virtual Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                // Sobrescribir JwtSettings:SecretKey para que el rate limiter
                // pueda validar los tokens generados por los tests (misma clave).
                // UseSetting asegura que los valores estÃ¡n disponibles durante la
                // ejecuciÃ³n de Program.cs (ConfigureAppConfiguration puede aplicarse despuÃ©s).
                builder.UseSetting("JwtSettings:SecretKey", JwtSecretKey);
                builder.UseSetting("JwtSettings:Issuer", JwtIssuer);
                builder.UseSetting("JwtSettings:Audience", JwtAudience);

                builder.ConfigureAppConfiguration((context, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["JwtSettings:SecretKey"] = JwtSecretKey,
                        ["JwtSettings:Issuer"] = JwtIssuer,
                        ["JwtSettings:Audience"] = JwtAudience
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Remove existing DbContext registration
                    var dbDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null)
                        services.Remove(dbDescriptor);

                    // Register DbContext pointing to Testcontainer PostgreSQL
                    services.AddDbContext<AppDbContext>((sp, options) =>
                    {
                        var tenantContext = sp.GetRequiredService<ITenantContext>();
                        options.UseNpgsql(Fixture.ConnectionString);
                    });

                    // Remover background services para evitar deadlocks con TRUNCATE CASCADE.
                    // BillingCutBackgroundService y EmailProcessorBackgroundService ejecutan queries
                    // periÃ³dicas que compiten por locks con CleanDatabaseAsync(), causando 40P01.
                    services.RemoveAll(typeof(IHostedService));

                    // Override JWT settings for testing
                    services.PostConfigure<Microsoft.Extensions.Options.IOptions<object>>(options => { });

                    // Configure JWT authentication to use test key
                    services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                        Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                        options =>
                        {
                            // Disable inbound claim mapping so "role" stays as "role"
                            // instead of being remapped to the long ClaimTypes.Role URI.
                            options.MapInboundClaims = false;

                            options.TokenValidationParameters = new TokenValidationParameters
                            {
                                ValidateIssuer = true,
                                ValidateAudience = true,
                                ValidateLifetime = true,
                                ValidateIssuerSigningKey = true,
                                ValidIssuer = JwtIssuer,
                                ValidAudience = JwtAudience,
                                IssuerSigningKey = new SymmetricSecurityKey(
                                    Encoding.UTF8.GetBytes(JwtSecretKey)),
                                ClockSkew = TimeSpan.Zero,
                                RoleClaimType = "role",
                                NameClaimType = "sub"
                            };
                        });
                });
            });

        Client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public virtual Task DisposeAsync()
    {
        Client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Generates a valid JWT token for a specific user with the given role and comercio.
    /// Incluye claim 'iat' para que JtiValidationMiddleware pueda verificar bloqueos a nivel de usuario.
    /// </summary>
    protected string GenerateTestToken(int usuarioId, int comercioId, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim("sub", usuarioId.ToString()),
            new Claim("comercio_id", comercioId.ToString()),
            new Claim("role", role),
            new Claim("jti", Guid.NewGuid().ToString()),
            new Claim("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Sets the authorization header with a valid JWT for the given user context.
    /// </summary>
    protected void AuthenticateAs(int usuarioId, int comercioId, string role)
    {
        var token = GenerateTestToken(usuarioId, comercioId, role);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Creates a fresh DbContext scoped to SuperAdmin for seeding/verification.
    /// </summary>
    protected AppDbContext CreateDbContext()
    {
        return Fixture.CreateDbContext();
    }

    /// <summary>
    /// Cleans up test data from the database between tests.
    /// Uses TRUNCATE CASCADE to reset all tables efficiently.
    /// Usa conexión directa (sin pool de EF Core) para evitar conexiones corruptas.
    /// </summary>
    protected async Task CleanDatabaseAsync()
    {
        // Usar conexion directa (sin pool de EF Core) para evitar que conexiones corruptas
        // en el pool causen NpgsqlException o timeouts transitorios durante TRUNCATE CASCADE.
        await using var conn = Fixture.CreateDirectConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 60;
        cmd.CommandText = @"
            DO $$ 
            DECLARE r RECORD;
            BEGIN
                FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public') LOOP
                    EXECUTE 'TRUNCATE TABLE ""' || r.tablename || '"" CASCADE';
                END LOOP;
            END $$;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserta un Comercio "Sistema", una Sucursal asociada y un Usuario SuperAdmin (Id=999)
    /// en la base de datos. Requerido por tests que autentican como SuperAdmin (Id=999)
    /// para satisfacer la validaciÃ³n de JtiValidationMiddleware (usuario debe existir en DB).
    /// DEBE llamarse DESPUÃ‰S de SeedPlansAsync() ya que Comercios tiene FK a Planes.
    /// Es idempotente: usa ON CONFLICT DO NOTHING para el INSERT del usuario.
    /// </summary>
    protected async Task SeedSuperAdminAsync()
    {
        await using var db = CreateDbContext();

        // 1. Insertar Comercio sistema (PlanId=3 = Empresarial)
        var comercioSistema = new SaasPOS.Domain.Entities.Comercio
        {
            Ruc = "0000000000001",
            RazonSocial = "Sistema (SuperAdmin)",
            PlanId = 3,
            UsaFacturacionSRI = false,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow
        };
        db.Comercios.Add(comercioSistema);
        await db.SaveChangesAsync();

        // 2. Insertar Sucursal asociada al comercio sistema
        var sucursalSistema = new SaasPOS.Domain.Entities.Sucursal
        {
            ComercioId = comercioSistema.Id,
            Nombre = "Sucursal Sistema"
        };
        db.Sucursales.Add(sucursalSistema);
        await db.SaveChangesAsync();

        // 3. Insertar Usuario SuperAdmin con Id=999 (idempotente)
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Usuarios"" (""Id"", ""ComercioId"", ""Nombre"", ""Email"", ""PasswordHash"", ""Rol"", ""Activo"")
              VALUES (999, {0}, 'SuperAdmin Test', 'superadmin@test.com', '$2a$12$dummyhashsuperadminvalue', 'SuperAdmin', true)
              ON CONFLICT (""Id"") DO NOTHING",
            comercioSistema.Id);

        // 4. Avanzar la secuencia para evitar conflictos de Id en inserciones futuras
        await db.Database.ExecuteSqlRawAsync(
            @"SELECT setval(pg_get_serial_sequence('""Usuarios""', 'Id'), GREATEST((SELECT MAX(""Id"") FROM ""Usuarios""), 999))");
    }
}
