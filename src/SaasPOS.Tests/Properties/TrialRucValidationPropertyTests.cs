using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests para validación de RUC en la creación de trial.
/// Feature: prueba-gratuita, Property 3: Validación de RUC
/// **Validates: Requirements 1.7**
///
/// Para cualquier string que NO sea exactamente 13 dígitos numéricos,
/// la creación de trial SHALL ser rechazada con error "RUC_INVALIDO"
/// sin crear ningún registro en la base de datos.
/// </summary>
public class TrialRucValidationPropertyTests
{
    private static readonly Mock<ILogger<TrialService>> LoggerMock = new();

    /// <summary>
    /// Generador de RUC inválido: strings que no son exactamente 13 dígitos numéricos.
    /// Combina múltiples estrategias para generar valores inválidos.
    /// </summary>
    private static Gen<string> InvalidRucGen => Gen.OneOf(
        // Strings de dígitos con longitud menor a 13
        Gen.Choose(0, 12).SelectMany(len =>
            Gen.Choose(0, 9).ListOf(len).Select(digits =>
                new string(digits.Select(d => (char)('0' + d)).ToArray()))),
        // Strings de dígitos con longitud mayor a 13
        Gen.Choose(14, 30).SelectMany(len =>
            Gen.Choose(0, 9).ListOf(len).Select(digits =>
                new string(digits.Select(d => (char)('0' + d)).ToArray()))),
        // Strings de exactamente 13 caracteres pero con caracteres no numéricos
        Arb.Generate<NonEmptyString>()
            .Where(s => !System.Text.RegularExpressions.Regex.IsMatch(s.Get, @"^\d{13}$"))
            .Select(s => s.Get),
        // String vacío
        Gen.Constant(""),
        // Espacios en blanco
        Gen.Constant("   ")
    );

    private static Arbitrary<string> InvalidRucArbitrary =>
        Arb.From(InvalidRucGen);

    private static (AppDbContext db, TrialService service) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);
        var service = new TrialService(db, LoggerMock.Object);
        return (db, service);
    }

    /// <summary>
    /// Crea y persiste el Plan Básico necesario para la creación de trials.
    /// </summary>
    private static void SeedPlanBasico(AppDbContext db)
    {
        var plan = new Plan
        {
            Id = 1,
            Nombre = "Básico",
            Precio = 9.99m,
            LimiteUsuarios = 2,
            LimiteAtributos = 2,
            LimiteSucursales = 1
        };
        db.Planes.Add(plan);
        db.SaveChanges();
    }

    // ─── Property 3: RUC inválido siempre es rechazado ────────────────────────

    /// <summary>
    /// Para CUALQUIER string que no sea exactamente 13 dígitos numéricos,
    /// CrearTrialAsync lanza InvalidOperationException con mensaje "RUC_INVALIDO"
    /// y no se crea ningún registro en la base de datos.
    /// **Validates: Requirements 1.7**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(TrialRucValidationPropertyTests)])]
    public bool RucInvalido_SiempreEsRechazado(string invalidRuc)
    {
        var (db, service) = CreateContext();
        using (db)
        {
            // Preparar: seed del Plan Básico
            SeedPlanBasico(db);

            // Crear request con RUC inválido y RazonSocial válida
            var request = new CreateTrialRequest(invalidRuc, "Test Comercio");

            try
            {
                // Actuar: intentar crear el trial
                service.CrearTrialAsync(request, superAdminId: 1).GetAwaiter().GetResult();

                // Si no lanza excepción, la propiedad falla
                return false;
            }
            catch (InvalidOperationException ex)
            {
                // Verificar que el mensaje es "RUC_INVALIDO"
                var mensajeCorrecto = ex.Message == "RUC_INVALIDO";

                // Verificar que no se crearon registros
                var sinComercios = !db.Comercios.IgnoreQueryFilters().Any();
                var sinSuscripciones = !db.Suscripciones.IgnoreQueryFilters().Any();

                return mensajeCorrecto && sinComercios && sinSuscripciones;
            }
            catch
            {
                // Cualquier otra excepción es un fallo de la propiedad
                return false;
            }
        }
    }

    /// <summary>
    /// Registra el Arbitrary personalizado para que FsCheck lo use al generar strings.
    /// </summary>
    public static Arbitrary<string> String() => InvalidRucArbitrary;
}
