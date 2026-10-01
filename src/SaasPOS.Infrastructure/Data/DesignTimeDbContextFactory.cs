using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Infrastructure.Data;

/// <summary>
/// Factory para crear instancias de AppDbContext en design-time (migraciones EF Core).
/// Permite ejecutar comandos como `dotnet ef migrations add` y `dotnet ef database update`
/// sin necesidad de levantar el host completo de la aplicación.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Construir configuración desde appsettings.json del proyecto Api
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "SaasPOS.Api");

        // Si se ejecuta desde la raíz del proyecto Api (--startup-project), usar el directorio actual
        if (!Directory.Exists(basePath))
        {
            basePath = Directory.GetCurrentDirectory();
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        // Usar un TenantContext de design-time que actúa como SuperAdmin
        // para que los query filters no interfieran con la generación de migraciones
        return new AppDbContext(optionsBuilder.Options, new DesignTimeTenantContext());
    }
}

/// <summary>
/// Implementación de ITenantContext para design-time.
/// Simula un SuperAdmin para que los HasQueryFilter no bloqueen la generación de migraciones.
/// </summary>
internal class DesignTimeTenantContext : ITenantContext
{
    public int? ComercioId => null;
    public int? SucursalId => null;
    public bool IsSuperAdmin => true;
}
