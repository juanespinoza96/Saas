using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Tests de validación de migraciones EF Core contra una base de datos PostgreSQL limpia.
/// Verifica que las migraciones se aplican correctamente y que el esquema resultante
/// coincide con lo que EnsureCreated() genera.
/// Requisitos: 25.4, 25.5
/// </summary>
public class MigrationValidationTests : IAsyncLifetime
{
    private PostgreSqlContainer _migrateContainer = null!;
    private PostgreSqlContainer _ensureCreatedContainer = null!;

    public async Task InitializeAsync()
    {
        // Contenedor 1: para aplicar migraciones
        _migrateContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("saaspos_migrate")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        // Contenedor 2: para EnsureCreated (esquema de referencia)
        _ensureCreatedContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("saaspos_ensure")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await Task.WhenAll(
            _migrateContainer.StartAsync(),
            _ensureCreatedContainer.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            _migrateContainer.DisposeAsync().AsTask(),
            _ensureCreatedContainer.DisposeAsync().AsTask());
    }

    /// <summary>
    /// Crea un AppDbContext apuntando al contenedor indicado.
    /// </summary>
    private AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, new MigrationTestTenantContext());
    }

    /// <summary>
    /// Obtiene los nombres de tablas del esquema público, excluyendo tablas internas de EF.
    /// </summary>
    private async Task<List<string>> GetUserTablesAsync(AppDbContext context)
    {
        var tables = new List<string>();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT tablename 
            FROM pg_tables 
            WHERE schemaname = 'public' 
              AND tablename != '__EFMigrationsHistory'
            ORDER BY tablename;";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        await connection.CloseAsync();
        return tables;
    }

    [DockerAvailableFact]
    public async Task Migrate_AplicaTodasLasMigraciones_SinErrores()
    {
        // Arrange: base de datos completamente vacía
        await using var context = CreateContext(_migrateContainer.GetConnectionString());

        // Act: aplicar todas las migraciones pendientes
        await context.Database.MigrateAsync();

        // Assert: la base de datos tiene tablas creadas
        var tables = await GetUserTablesAsync(context);
        Assert.NotEmpty(tables);
    }

    [DockerAvailableFact]
    public async Task Migrate_CreaTabla_EFMigrationsHistory_ConRegistros()
    {
        // Arrange & Act
        await using var context = CreateContext(_migrateContainer.GetConnectionString());
        await context.Database.MigrateAsync();

        // Assert: verificar que __EFMigrationsHistory existe y tiene registros
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ""MigrationId"" 
            FROM ""__EFMigrationsHistory"" 
            ORDER BY ""MigrationId"";";

        var migrations = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            migrations.Add(reader.GetString(0));
        }

        await connection.CloseAsync();

        // Debe contener la baseline única del saneamiento (InitialBaseline)
        Assert.Contains(migrations, m => m.Contains("InitialBaseline"));
        // Debe contener todas las migraciones del proyecto
        Assert.True(migrations.Count >= 1,
            "Debe haber al menos una migración registrada en __EFMigrationsHistory");
    }

    [DockerAvailableFact]
    public async Task Migrate_CreaTodasLasTablasEsperadas()
    {
        // Arrange & Act
        await using var context = CreateContext(_migrateContainer.GetConnectionString());
        await context.Database.MigrateAsync();

        // Assert: verificar que todas las tablas esperadas del dominio existen
        var tables = await GetUserTablesAsync(context);

        // Tablas esperadas basadas en los DbSet del AppDbContext.
        // Tras el saneamiento, ConfiguracionesComprobante es descubrible por MigrateAsync()
        // (su CREATE vive en el Up() de InitialBaseline), por lo que se exige como el resto.
        var tablasEsperadas = new[]
        {
            "Planes",
            "Comercios",
            "ConfiguracionesComercio",
            "ConfiguracionesComprobante",
            "Suscripciones",
            "PagosComercio",
            "Sucursales",
            "Usuarios",
            "Categorias",
            "AtributosCategoria",
            "Productos",
            "RecetasProducto",
            "PreciosVolumen",
            "StockSucursal",
            "Clientes",
            "Ventas",
            "DetalleVentas",
            "Notificaciones",
            "ColaCorreos",
            "LogsAuditoria",
            "ConfiguracionesSucursal",
            "SolicitudesRecuperacion"
        };

        foreach (var tabla in tablasEsperadas)
        {
            Assert.Contains(tabla, tables,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [DockerAvailableFact]
    public async Task Migrate_EsquemaCoincideConEnsureCreated()
    {
        // Arrange: aplicar migraciones en contenedor 1
        await using var migrateContext = CreateContext(_migrateContainer.GetConnectionString());
        await migrateContext.Database.MigrateAsync();

        // Aplicar EnsureCreated en contenedor 2 (esquema de referencia)
        await using var ensureContext = CreateContext(_ensureCreatedContainer.GetConnectionString());
        await ensureContext.Database.EnsureCreatedAsync();

        // Act: obtener tablas de ambos esquemas
        var tablasMigrate = await GetUserTablesAsync(migrateContext);
        var tablasEnsure = await GetUserTablesAsync(ensureContext);

        // Assert: los nombres de tablas deben coincidir
        // (case-insensitive para mayor tolerancia)
        var migrateNormalized = tablasMigrate
            .Select(t => t.ToLowerInvariant())
            .OrderBy(t => t)
            .ToList();

        var ensureNormalized = tablasEnsure
            .Select(t => t.ToLowerInvariant())
            .OrderBy(t => t)
            .ToList();

        // Tras el saneamiento de la cadena (baseline única InitialBaseline), la tabla
        // ConfiguracionesComprobante YA es descubrible por MigrateAsync() (su CREATE vive
        // en el Up() de InitialBaseline). Por eso se compara como cualquier otra tabla,
        // sin exclusiones: el esquema de Migrate debe ser equivalente al de EnsureCreated.
        var faltantesEnMigrate = ensureNormalized
            .Except(migrateNormalized)
            .ToList();

        Assert.True(faltantesEnMigrate.Count == 0,
            $"Tablas en EnsureCreated que faltan en Migrate: {string.Join(", ", faltantesEnMigrate)}");

        // Las tablas del Migrate que también están en EnsureCreated deben coincidir
        var sobrantesEnMigrate = migrateNormalized.Except(ensureNormalized).ToList();
        Assert.True(sobrantesEnMigrate.Count == 0,
            $"Tablas sobrantes en Migrate: {string.Join(", ", sobrantesEnMigrate)}");
    }

    [DockerAvailableFact]
    public async Task Migrate_RegistraTodasLasMigracionesDelProyecto()
    {
        // Arrange & Act
        await using var context = CreateContext(_migrateContainer.GetConnectionString());
        await context.Database.MigrateAsync();

        // Assert: verificar que no hay migraciones pendientes después de aplicar
        var pendientes = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pendientes);

        // Verificar que las migraciones aplicadas coinciden con las del proyecto
        var aplicadas = await context.Database.GetAppliedMigrationsAsync();
        var disponibles = context.Database.GetMigrations();

        Assert.Equal(
            disponibles.OrderBy(m => m),
            aplicadas.OrderBy(m => m));
    }
}

/// <summary>
/// Implementación de ITenantContext para los tests de migración.
/// Actúa como SuperAdmin para que los query filters no interfieran.
/// </summary>
internal class MigrationTestTenantContext : ITenantContext
{
    public int? ComercioId => null;
    public int? SucursalId => null;
    public bool IsSuperAdmin => true;
}
