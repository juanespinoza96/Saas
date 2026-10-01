using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

// Feature: mejoras-operativas-v2, Property 9: Filtrado de sugerencias por número de sucursales
/// <summary>
/// Property-based tests para el filtrado de sugerencias por número de sucursales.
/// **Validates: Requirements 7.3**
///
/// Property 9: Filtrado de sugerencias por número de sucursales
/// "For any comercio con N sucursales, las sugerencias del Chat IA SHALL incluir preguntas
/// de comparación entre sucursales si y solo si N > 1."
/// </summary>
public class ChatBISugerenciasSucursalesPropertyTests
{
    /// <summary>
    /// Roles válidos en el sistema para generar datos aleatorios.
    /// </summary>
    private static readonly string[] RolesValidos =
    {
        "Dueño", "Gerente", "Supervisor", "Cajero", "Bodeguero"
    };

    /// <summary>
    /// Crea una instancia del servicio con dependencias nulas/mock
    /// dado que ObtenerSugerencias es una función pura que no las necesita.
    /// </summary>
    private static ChatBusinessIntelligenceService CrearServicio()
    {
        var aiServiceMock = new Mock<IAIService>();
        var loggerMock = new Mock<ILogger<ChatBusinessIntelligenceService>>();

        // AppDbContext no se usa en ObtenerSugerencias, pasamos null con cast seguro
        return new ChatBusinessIntelligenceService(
            aiServiceMock.Object,
            null!,
            loggerMock.Object);
    }

    #region Property 9a: Si numSucursales > 1, las sugerencias DEBEN contener comparacion_sucursales

    /// <summary>
    /// Property 9a: Para cualquier numSucursales > 1 y cualquier rol válido,
    /// las sugerencias devueltas deben incluir al menos una sugerencia con categoría "comparacion_sucursales".
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConMultiplesSucursales_IncluyeComparacion()
    {
        // Generador: numSucursales entre 2 y 100, rol aleatorio
        var numSucursalesGen = Gen.Choose(2, 100);
        var rolGen = Gen.Elements(RolesValidos);

        return Prop.ForAll(
            numSucursalesGen.ToArbitrary(),
            rolGen.ToArbitrary(),
            (int numSucursales, string rol) =>
            {
                var service = CrearServicio();

                var sugerencias = service.ObtenerSugerencias(numSucursales, rol);

                var tieneComparacion = sugerencias.Any(s => s.Categoria == "comparacion_sucursales");

                return tieneComparacion.ToProperty()
                    .Label($"Con numSucursales={numSucursales} y rol='{rol}', " +
                           $"se esperaba al menos una sugerencia de 'comparacion_sucursales' " +
                           $"pero no se encontró ninguna. Total sugerencias: {sugerencias.Count}");
            });
    }

    #endregion

    #region Property 9b: Si numSucursales <= 1, las sugerencias NO DEBEN contener comparacion_sucursales

    /// <summary>
    /// Property 9b: Para cualquier numSucursales <= 1 y cualquier rol válido,
    /// las sugerencias devueltas NO deben incluir ninguna sugerencia con categoría "comparacion_sucursales".
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConUnaSucursalOMenos_NoIncluyeComparacion()
    {
        // Generador: numSucursales 0 o 1, rol aleatorio
        var numSucursalesGen = Gen.Choose(0, 1);
        var rolGen = Gen.Elements(RolesValidos);

        return Prop.ForAll(
            numSucursalesGen.ToArbitrary(),
            rolGen.ToArbitrary(),
            (int numSucursales, string rol) =>
            {
                var service = CrearServicio();

                var sugerencias = service.ObtenerSugerencias(numSucursales, rol);

                var noTieneComparacion = !sugerencias.Any(s => s.Categoria == "comparacion_sucursales");

                return noTieneComparacion.ToProperty()
                    .Label($"Con numSucursales={numSucursales} y rol='{rol}', " +
                           $"NO se esperaban sugerencias de 'comparacion_sucursales' " +
                           $"pero se encontraron {sugerencias.Count(s => s.Categoria == "comparacion_sucursales")}");
            });
    }

    #endregion

    #region Property 9c: Bicondicional completo - comparacion_sucursales si y solo si numSucursales > 1

    /// <summary>
    /// Property 9c: Para cualquier valor de numSucursales (1 a 100) y cualquier rol válido,
    /// la presencia de sugerencias "comparacion_sucursales" es equivalente a numSucursales > 1.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Bicondicional_ComparacionSucursales_SiYSoloSi_MasDeUnaSucursal()
    {
        // Generador: numSucursales entre 1 y 100 (incluye borde 1), rol aleatorio
        var numSucursalesGen = Gen.Choose(1, 100);
        var rolGen = Gen.Elements(RolesValidos);

        return Prop.ForAll(
            numSucursalesGen.ToArbitrary(),
            rolGen.ToArbitrary(),
            (int numSucursales, string rol) =>
            {
                var service = CrearServicio();

                var sugerencias = service.ObtenerSugerencias(numSucursales, rol);

                var tieneComparacion = sugerencias.Any(s => s.Categoria == "comparacion_sucursales");
                var deberiaIncluir = numSucursales > 1;

                // Bicondicional: tieneComparacion == deberiaIncluir
                return (tieneComparacion == deberiaIncluir).ToProperty()
                    .Label($"Bicondicional violado: numSucursales={numSucursales}, rol='{rol}', " +
                           $"tieneComparacion={tieneComparacion}, deberiaIncluir={deberiaIncluir}");
            });
    }

    #endregion
}
