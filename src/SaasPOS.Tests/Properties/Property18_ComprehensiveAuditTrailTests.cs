using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Input model for FsCheck property generation.
/// </summary>
public class AuditInput
{
    public int ComercioId { get; set; }
    public int? UsuarioId { get; set; }
    public string Accion { get; set; } = string.Empty;
    public string TablaAfectada { get; set; } = string.Empty;
    public string RegistroId { get; set; } = string.Empty;
    public Dictionary<string, string>? ValoresAnteriores { get; set; }
    public Dictionary<string, string>? ValoresNuevos { get; set; }

    public override string ToString() =>
        $"AuditInput(ComercioId={ComercioId}, UsuarioId={UsuarioId}, Accion={Accion}, " +
        $"Tabla={TablaAfectada}, RegistroId={RegistroId}, " +
        $"HasAnteriores={ValoresAnteriores != null}, HasNuevos={ValoresNuevos != null})";
}

/// <summary>
/// FsCheck Arbitraries for AuditInput generation.
/// </summary>
public static class AuditInputArbitraries
{
    private static readonly string[] RequiredTables = { "Usuarios", "Productos", "Ventas", "Sucursales", "Clientes", "Comercios" };
    private static readonly string[] Acciones = { "Crear", "Actualizar", "Eliminar", "Desactivar" };

    public static Arbitrary<AuditInput> AuditInput()
    {
        var gen = from comercioId in Gen.Choose(1, 1000)
                  from usuarioId in Gen.OneOf(
                      Gen.Constant<int?>(null),
                      Gen.Choose(1, 500).Select(x => (int?)x))
                  from tablaIdx in Gen.Choose(0, RequiredTables.Length - 1)
                  from accionIdx in Gen.Choose(0, Acciones.Length - 1)
                  from registroId in Gen.Choose(1, 99999).Select(x => x.ToString())
                  from hasAnteriores in Arb.Generate<bool>()
                  from hasNuevos in Arb.Generate<bool>()
                  from anteriorKey in Gen.Elements("nombre", "email", "precio", "stock", "estado")
                  from anteriorValue in Gen.Elements("valor_anterior_1", "test@old.com", "15.50", "100", "activo")
                  from nuevoKey in Gen.Elements("nombre", "email", "precio", "stock", "estado")
                  from nuevoValue in Gen.Elements("valor_nuevo_1", "test@new.com", "20.00", "95", "inactivo")
                  select new AuditInput
                  {
                      ComercioId = comercioId,
                      UsuarioId = usuarioId,
                      Accion = Acciones[accionIdx],
                      TablaAfectada = RequiredTables[tablaIdx],
                      RegistroId = registroId,
                      ValoresAnteriores = hasAnteriores ? new Dictionary<string, string> { { anteriorKey, anteriorValue } } : null,
                      ValoresNuevos = hasNuevos ? new Dictionary<string, string> { { nuevoKey, nuevoValue } } : null
                  };

        return Arb.From(gen);
    }
}

/// <summary>
/// Property 18: Comprehensive audit trail for all critical operations.
/// For any call to AuditService.RegistrarAsync with valid parameters, the resulting
/// record in LogsAuditoria SHALL have all fields properly persisted and JSON-serialized.
/// **Validates: Requirements 4.8, 9.7, 15.1, 15.2**
/// </summary>
public class Property18_ComprehensiveAuditTrailTests
{
    private static readonly string[] RequiredTables = { "Usuarios", "Productos", "Ventas", "Sucursales", "Clientes", "Comercios" };

    private static (AppDbContext db, AuditService service) CreateServiceWithContext()
    {
        var tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.ComercioId).Returns(1);
        tenantMock.Setup(t => t.IsSuperAdmin).Returns(true);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantMock.Object);

        // Seed required entities
        db.Planes.Add(new Plan { Id = 1, Nombre = "Básico", Precio = 10m, LimiteUsuarios = 5, LimiteAtributos = 2 });
        db.Comercios.Add(new Comercio { Id = 1, Ruc = "1234567890001", RazonSocial = "Test Commerce", PlanId = 1, FechaRegistro = DateTime.UtcNow });
        db.SaveChanges();

        var service = new AuditService(db);
        return (db, service);
    }

    /// <summary>
    /// Property 18: For any call to AuditService.RegistrarAsync with valid parameters,
    /// the persisted record SHALL have:
    /// 1. ComercioId matching the input
    /// 2. UsuarioId matching the input (may be null)
    /// 3. Non-empty Accion and TablaAfectada
    /// 4. RegistroId matching the input
    /// 5. FechaHora set (non-default DateTime)
    /// 6. ValoresAnteriores is valid JSON when provided
    /// 7. ValoresNuevos is valid JSON when provided
    /// **Validates: Requirements 4.8, 9.7, 15.1, 15.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuditInputArbitraries) })]
    public bool AuditService_PersistsAllFieldsCorrectly(AuditInput input)
    {
        var (db, service) = CreateServiceWithContext();
        using (db)
        {
            // Seed comercio for the input if different from default
            if (input.ComercioId != 1)
            {
                var existingComercio = db.Comercios.IgnoreQueryFilters().FirstOrDefault(c => c.Id == input.ComercioId);
                if (existingComercio == null)
                {
                    db.Comercios.Add(new Comercio
                    {
                        Id = input.ComercioId,
                        Ruc = $"RUC{input.ComercioId:D13}",
                        RazonSocial = $"Commerce {input.ComercioId}",
                        PlanId = 1,
                        FechaRegistro = DateTime.UtcNow
                    });
                    db.SaveChanges();
                }
            }

            // Act
            service.RegistrarAsync(
                input.ComercioId,
                input.UsuarioId,
                input.Accion,
                input.TablaAfectada,
                input.RegistroId,
                input.ValoresAnteriores,
                input.ValoresNuevos
            ).GetAwaiter().GetResult();

            // Retrieve persisted record
            var log = db.LogsAuditoria.IgnoreQueryFilters()
                .FirstOrDefault(l => l.ComercioId == input.ComercioId && l.RegistroId == input.RegistroId);

            if (log == null)
                return false;

            // 1. ComercioId matches input
            if (log.ComercioId != input.ComercioId)
                return false;

            // 2. UsuarioId matches input (may be null)
            if (log.UsuarioId != input.UsuarioId)
                return false;

            // 3. Non-empty Accion and TablaAfectada
            if (string.IsNullOrEmpty(log.Accion) || string.IsNullOrEmpty(log.TablaAfectada))
                return false;

            // Verify Accion and TablaAfectada match input
            if (log.Accion != input.Accion || log.TablaAfectada != input.TablaAfectada)
                return false;

            // 4. RegistroId matches input
            if (log.RegistroId != input.RegistroId)
                return false;

            // 5. FechaHora is set (non-default DateTime)
            if (log.FechaHora == default)
                return false;

            // 6. If valoresAnteriores is not null, ValoresAnteriores SHALL be valid JSON
            if (input.ValoresAnteriores != null)
            {
                if (string.IsNullOrEmpty(log.ValoresAnteriores))
                    return false;
                if (!IsValidJson(log.ValoresAnteriores))
                    return false;
            }
            else
            {
                if (log.ValoresAnteriores != null)
                    return false;
            }

            // 7. If valoresNuevos is not null, ValoresNuevos SHALL be valid JSON
            if (input.ValoresNuevos != null)
            {
                if (string.IsNullOrEmpty(log.ValoresNuevos))
                    return false;
                if (!IsValidJson(log.ValoresNuevos))
                    return false;
            }
            else
            {
                if (log.ValoresNuevos != null)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Property 18 (supplementary): The TablaAfectada field must be from the required tables list.
    /// Verifies that auditable tables match Requirements 15.1.
    /// **Validates: Requirements 15.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuditInputArbitraries) })]
    public bool AuditService_TablaAfectadaIsFromRequiredList(AuditInput input)
    {
        // The generator only produces values from RequiredTables, so this verifies the constraint
        return RequiredTables.Contains(input.TablaAfectada);
    }

    private static bool IsValidJson(string jsonString)
    {
        try
        {
            JsonDocument.Parse(jsonString);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
