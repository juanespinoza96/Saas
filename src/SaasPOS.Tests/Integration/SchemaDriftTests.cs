using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Validación automática de deriva de esquema (schema drift) de EF Core.
///
/// A diferencia del resto de la suite de integración (que monta el esquema con
/// EnsureCreated()), este test levanta una base PostgreSQL LIMPIA y aplica las
/// MIGRACIONES reales (MigrateAsync()), que son la fuente de verdad del esquema.
/// Luego verifica dos cosas:
///   1. Que __EFMigrationsHistory contiene la migración del Frente 1 (InitialBaseline).
///   2. Que, con esas migraciones aplicadas, el modelo de EF NO tiene cambios
///      pendientes (HasPendingModelChanges() == false), es decir, no hay deriva
///      entre lo que generan las migraciones y lo que describe el modelo actual.
///
/// --- Por qué HasPendingModelChanges() y NO inspección de information_schema ---
/// Se usa deliberadamente HasPendingModelChanges() en lugar de recorrer
/// information_schema columna por columna porque esa inspección manual es FRÁGIL:
///   - Depende de los detalles de mapeo de Npgsql (nombres de tipos como
///     "timestamp with time zone" vs "timestamptz", precisión/escala de numéricos,
///     colaciones, longitudes por defecto), que pueden variar entre versiones del
///     proveedor sin que exista deriva real.
///   - Es propensa a falsos positivos (diferencias cosméticas de nomenclatura o de
///     metadatos que no implican una divergencia de esquema) y a falsos negativos
///     (diferencias que information_schema no expone bien, como ciertos índices o
///     restricciones).
///   - Obliga a mantener a mano una lista de columnas/tipos que se desincroniza con
///     el modelo en cada cambio.
/// HasPendingModelChanges() delega esa comparación al propio motor de EF Core, que
/// contrasta el snapshot de las migraciones contra el modelo actual con la misma
/// lógica que usa `dotnet ef migrations has-pending-model-changes`. Es la fuente de
/// verdad robusta y libre de los falsos positivos de la inspección por catálogo.
///
/// Nota: en la tarea 12 este test se amplía para esperar las 2 migraciones
/// (InitialBaseline + AddJtiRevocados). En esta etapa solo se espera InitialBaseline.
/// Requisitos: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6
/// </summary>
public class SchemaDriftTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        // Contenedor PostgreSQL vacío: base LIMPIA sobre la que se aplicarán migraciones.
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("saaspos_drift")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Construye un AppDbContext apuntando al contenedor limpio, con un ITenantContext
    /// SuperAdmin (mismo truco que DesignTimeDbContextFactory) para que los query filters
    /// de tenant no interfieran con la aplicación de migraciones ni con la introspección
    /// del modelo.
    /// </summary>
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new AppDbContext(options, new SchemaDriftTenantContext());
    }

    [DockerAvailableFact]
    public async Task Migraciones_AplicadasSobreBaseLimpia_SinDerivaDeEsquema()
    {
        // Arrange: base de datos completamente limpia (contenedor recién levantado).
        await using var context = CreateContext();

        // Act: aplicar las migraciones reales (NO EnsureCreated).
        await context.Database.MigrateAsync();

        // Assert 1: __EFMigrationsHistory contiene la migración del Frente 1.
        // En esta etapa se espera exactamente 1 migración: InitialBaseline.
        var migracionesAplicadas = (await context.Database.GetAppliedMigrationsAsync()).ToList();

        Assert.Single(migracionesAplicadas);
        Assert.Contains(migracionesAplicadas, m => m.Contains("InitialBaseline"));

        // Assert 2: no hay deriva de esquema. Con las migraciones aplicadas, el modelo
        // de EF Core no debe reportar cambios pendientes. Ver el bloque de comentario de
        // la clase para la justificación de usar HasPendingModelChanges() y no
        // information_schema.
        Assert.False(context.Database.HasPendingModelChanges());
    }
}

/// <summary>
/// Implementación de ITenantContext para el test de deriva de esquema.
/// Actúa como SuperAdmin para que los query filters no interfieran (igual que
/// DesignTimeDbContextFactory en design-time).
/// </summary>
internal class SchemaDriftTenantContext : ITenantContext
{
    public int? ComercioId => null;
    public int? SucursalId => null;
    public bool IsSuperAdmin => true;
}
