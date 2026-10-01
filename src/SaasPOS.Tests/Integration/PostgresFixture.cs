using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Shared fixture que levanta un contenedor PostgreSQL con Testcontainers,
/// aplica el schema de EF Core y expone el connection string para tests de integración.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("saaspos_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    /// <summary>
    /// Connection string con timeout extendido para evitar timeouts transitorios bajo carga.
    /// </summary>
    public string ConnectionString =>
        _container.GetConnectionString() + ";Command Timeout=60;Timeout=30;Include Error Detail=true";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Aplicar schema usando EnsureCreated (no hay migraciones en este proyecto)
        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public AppDbContext CreateDbContext(ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
                npgsqlOptions.CommandTimeout(60);
            })
            .Options;

        return new AppDbContext(options, tenantContext ?? new SuperAdminTenantContext());
    }

    /// <summary>
    /// Crea una conexión directa Npgsql (sin EF Core) para operaciones de mantenimiento
    /// como TRUNCATE CASCADE que no deben verse afectadas por el connection pool de EF Core.
    /// </summary>
    public NpgsqlConnection CreateDirectConnection()
    {
        return new NpgsqlConnection(ConnectionString + ";Pooling=false");
    }
}

/// <summary>
/// Tenant context que actúa como SuperAdmin — bypasses all query filters.
/// </summary>
internal class SuperAdminTenantContext : ITenantContext
{
    public int? ComercioId => null;
    public int? SucursalId => null;
    public bool IsSuperAdmin => true;
}

/// <summary>
/// Tenant context scoped a un Comercio específico — usado para queries con filtro de tenant.
/// </summary>
internal class ScopedTenantContext : ITenantContext
{
    public ScopedTenantContext(int comercioId)
    {
        ComercioId = comercioId;
    }

    public int? ComercioId { get; }
    public int? SucursalId => null;
    public bool IsSuperAdmin => false;
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
