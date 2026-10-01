using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Prueba de integración de esquema/migración para la funcionalidad de
/// recuperación de contraseña jerárquica (tarea 3.1, valida tareas 1 y 2).
///
/// Aplica la migración EF Core (incluida AddPasswordRecoveryFields) sobre un
/// contenedor PostgreSQL limpio con Testcontainers y verifica que las columnas
/// añadidas existen con el tipo y nulabilidad esperados.
///
/// Requisitos: 16.1, 16.2, 16.3, 16.4
/// </summary>
public class PasswordRecoverySchemaMigrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        // Contenedor PostgreSQL limpio: sin esquema previo, para aplicar migraciones desde cero.
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("saaspos_pwd_recovery")
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
    /// Crea un AppDbContext apuntando al contenedor de prueba.
    /// Ignora PendingModelChangesWarning porque no es relevante para la validación de esquema.
    /// </summary>
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, new PasswordRecoveryMigrationTenantContext());
    }

    /// <summary>
    /// Consulta information_schema.columns para obtener (tipo, nulabilidad) de una columna.
    /// Devuelve null si la columna no existe en la tabla indicada.
    /// </summary>
    private static async Task<(string DataType, bool IsNullable)?> GetColumnInfoAsync(
        AppDbContext context, string tabla, string columna)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @tabla
              AND column_name = @columna;";

        var pTabla = command.CreateParameter();
        pTabla.ParameterName = "tabla";
        pTabla.Value = tabla;
        command.Parameters.Add(pTabla);

        var pColumna = command.CreateParameter();
        pColumna.ParameterName = "columna";
        pColumna.Value = columna;
        command.Parameters.Add(pColumna);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var dataType = reader.GetString(0);
        var isNullable = reader.GetString(1) == "YES";
        return (dataType, isNullable);
    }

    /// <summary>
    /// Verifica que al aplicar todas las migraciones sobre un contenedor limpio,
    /// la migración AddPasswordRecoveryFields queda registrada en el historial.
    /// _Requirements: 16.4_
    /// </summary>
    [DockerAvailableFact]
    public async Task Migrate_RegistraMigracionAddPasswordRecoveryFields()
    {
        // Arrange: base de datos completamente vacía.
        await using var context = CreateContext();

        // Act: aplicar todas las migraciones pendientes.
        await context.Database.MigrateAsync();

        // Assert: la migración de recuperación de contraseña está aplicada.
        var aplicadas = await context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(aplicadas, m => m.Contains("AddPasswordRecoveryFields"));
    }

    /// <summary>
    /// Verifica que la columna Usuarios.DebeCambiarPassword existe como booleana no nula.
    /// _Requirements: 16.1_
    /// </summary>
    [DockerAvailableFact]
    public async Task Migrate_CreaColumna_Usuarios_DebeCambiarPassword()
    {
        // Arrange & Act
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // Assert
        var info = await GetColumnInfoAsync(context, "Usuarios", "DebeCambiarPassword");
        Assert.NotNull(info);
        Assert.Equal("boolean", info!.Value.DataType);
        Assert.False(info.Value.IsNullable, "DebeCambiarPassword debe ser NOT NULL (default false).");
    }

    /// <summary>
    /// Verifica que la columna SolicitudesRecuperacion.PasswordTemporalCifrada existe
    /// como texto nullable.
    /// _Requirements: 16.2_
    /// </summary>
    [DockerAvailableFact]
    public async Task Migrate_CreaColumna_SolicitudesRecuperacion_PasswordTemporalCifrada()
    {
        // Arrange & Act
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // Assert
        var info = await GetColumnInfoAsync(context, "SolicitudesRecuperacion", "PasswordTemporalCifrada");
        Assert.NotNull(info);
        Assert.Equal("character varying", info!.Value.DataType);
        Assert.True(info.Value.IsNullable, "PasswordTemporalCifrada debe admitir valor nulo.");
    }

    /// <summary>
    /// Verifica que la columna SolicitudesRecuperacion.FechaExpiracion existe
    /// como timestamp nullable.
    /// _Requirements: 16.2_
    /// </summary>
    [DockerAvailableFact]
    public async Task Migrate_CreaColumna_SolicitudesRecuperacion_FechaExpiracion()
    {
        // Arrange & Act
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // Assert
        var info = await GetColumnInfoAsync(context, "SolicitudesRecuperacion", "FechaExpiracion");
        Assert.NotNull(info);
        Assert.Equal("timestamp with time zone", info!.Value.DataType);
        Assert.True(info.Value.IsNullable, "FechaExpiracion debe admitir valor nulo.");
    }

    /// <summary>
    /// Verifica que la columna SolicitudesRecuperacion.MotivoRechazo existe
    /// como texto nullable.
    /// _Requirements: 16.3_
    /// </summary>
    [DockerAvailableFact]
    public async Task Migrate_CreaColumna_SolicitudesRecuperacion_MotivoRechazo()
    {
        // Arrange & Act
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // Assert
        var info = await GetColumnInfoAsync(context, "SolicitudesRecuperacion", "MotivoRechazo");
        Assert.NotNull(info);
        Assert.Equal("character varying", info!.Value.DataType);
        Assert.True(info.Value.IsNullable, "MotivoRechazo debe admitir valor nulo.");
    }

    /// <summary>
    /// Verifica de forma agregada que las cuatro columnas de recuperación de contraseña
    /// existen tras aplicar la migración sobre un contenedor limpio.
    /// _Requirements: 16.1, 16.2, 16.3, 16.4_
    /// </summary>
    [DockerAvailableFact]
    public async Task Migrate_CreaTodasLasColumnasDeRecuperacionPassword()
    {
        // Arrange & Act
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // Assert: las cuatro columnas nuevas deben existir.
        var columnasEsperadas = new (string Tabla, string Columna)[]
        {
            ("Usuarios", "DebeCambiarPassword"),
            ("SolicitudesRecuperacion", "PasswordTemporalCifrada"),
            ("SolicitudesRecuperacion", "FechaExpiracion"),
            ("SolicitudesRecuperacion", "MotivoRechazo")
        };

        foreach (var (tabla, columna) in columnasEsperadas)
        {
            var info = await GetColumnInfoAsync(context, tabla, columna);
            Assert.True(info != null, $"La columna {tabla}.{columna} debe existir tras la migración.");
        }
    }
}

/// <summary>
/// Implementación de ITenantContext para la prueba de esquema/migración.
/// Actúa como SuperAdmin para que los query filters no interfieran.
/// </summary>
internal class PasswordRecoveryMigrationTenantContext : ITenantContext
{
    public int? ComercioId => null;
    public int? SucursalId => null;
    public bool IsSuperAdmin => true;
}
