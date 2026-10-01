using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

// Feature: mejoras-operativas-v2, Property 10: Filtrado de sugerencias por rol
/// <summary>
/// Property-based tests para el filtrado de sugerencias del Chat IA por rol de usuario.
/// **Validates: Requirements 7.4**
///
/// Property 10: Filtrado de sugerencias por rol
/// "For any usuario con rol R, las sugerencias del Chat IA sobre gestión de inventario
/// SHALL aparecer si y solo si R ∈ {Gerente, Dueño, Bodeguero}."
/// </summary>
public class ChatBISugerenciasPropertyTests
{
    /// <summary>
    /// Roles que tienen acceso a sugerencias de inventario.
    /// </summary>
    private static readonly string[] RolesConInventario = { "Gerente", "Dueño", "Bodeguero" };

    /// <summary>
    /// Roles que NO tienen acceso a sugerencias de inventario.
    /// </summary>
    private static readonly string[] RolesSinInventario = { "Cajero", "Supervisor" };

    /// <summary>
    /// Todos los roles válidos del sistema para esta propiedad.
    /// </summary>
    private static readonly string[] TodosLosRoles = RolesConInventario.Concat(RolesSinInventario).ToArray();

    /// <summary>
    /// Crea una instancia del servicio con dependencias nulas/mock
    /// ya que ObtenerSugerencias es una función pura que no las utiliza.
    /// </summary>
    private static ChatBusinessIntelligenceService CrearServicio()
    {
        var aiServiceMock = new Mock<IAIService>();
        var loggerMock = new Mock<ILogger<ChatBusinessIntelligenceService>>();
        // ObtenerSugerencias no accede a la base de datos, pasamos null para el DbContext
        return new ChatBusinessIntelligenceService(aiServiceMock.Object, null!, loggerMock.Object);
    }

    #region Property 10a: Roles con acceso a inventario reciben sugerencias de inventario

    /// <summary>
    /// Property 10a: Para cualquier rol en {Gerente, Dueño, Bodeguero} y cualquier numSucursales,
    /// las sugerencias DEBEN contener al menos una con categoría "inventario".
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RolesConAccesoInventario_RecibenSugerenciasDeInventario()
    {
        // Generador: rol con acceso a inventario + número de sucursales positivo
        var rolGen = Gen.Elements(RolesConInventario);
        var numSucursalesGen = Gen.Choose(1, 100);

        return Prop.ForAll(rolGen.ToArbitrary(), numSucursalesGen.ToArbitrary(), (string rol, int numSucursales) =>
        {
            var servicio = CrearServicio();

            var sugerencias = servicio.ObtenerSugerencias(numSucursales, rol);

            // Debe contener al menos una sugerencia con categoría "inventario"
            var tieneInventario = sugerencias.Any(s => s.Categoria == "inventario");

            return tieneInventario.ToProperty()
                .Label($"Rol '{rol}' con {numSucursales} sucursales debería recibir sugerencias de inventario, " +
                       $"pero no recibió ninguna. Categorías recibidas: [{string.Join(", ", sugerencias.Select(s => s.Categoria).Distinct())}]");
        });
    }

    #endregion

    #region Property 10b: Roles sin acceso a inventario NO reciben sugerencias de inventario

    /// <summary>
    /// Property 10b: Para cualquier rol en {Cajero, Supervisor} y cualquier numSucursales,
    /// las sugerencias NO DEBEN contener ninguna con categoría "inventario".
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RolesSinAccesoInventario_NoRecibenSugerenciasDeInventario()
    {
        // Generador: rol sin acceso a inventario + número de sucursales positivo
        var rolGen = Gen.Elements(RolesSinInventario);
        var numSucursalesGen = Gen.Choose(1, 100);

        return Prop.ForAll(rolGen.ToArbitrary(), numSucursalesGen.ToArbitrary(), (string rol, int numSucursales) =>
        {
            var servicio = CrearServicio();

            var sugerencias = servicio.ObtenerSugerencias(numSucursales, rol);

            // NO debe contener sugerencias con categoría "inventario"
            var noTieneInventario = !sugerencias.Any(s => s.Categoria == "inventario");

            return noTieneInventario.ToProperty()
                .Label($"Rol '{rol}' con {numSucursales} sucursales NO debería recibir sugerencias de inventario, " +
                       $"pero recibió {sugerencias.Count(s => s.Categoria == "inventario")} sugerencias de inventario.");
        });
    }

    #endregion

    #region Property 10c: Bicondicional completo - sugerencias de inventario si y solo si rol permitido

    /// <summary>
    /// Property 10c: Para cualquier rol del sistema y cualquier numSucursales,
    /// las sugerencias contienen "inventario" si y solo si rol ∈ {Gerente, Dueño, Bodeguero}.
    /// Verifica el bicondicional completo de la propiedad.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FiltradoInventario_Bicondicional_RolPermitido()
    {
        // Generador: cualquier rol válido + número de sucursales positivo
        var rolGen = Gen.Elements(TodosLosRoles);
        var numSucursalesGen = Gen.Choose(1, 100);

        return Prop.ForAll(rolGen.ToArbitrary(), numSucursalesGen.ToArbitrary(), (string rol, int numSucursales) =>
        {
            var servicio = CrearServicio();

            var sugerencias = servicio.ObtenerSugerencias(numSucursales, rol);

            var tieneInventario = sugerencias.Any(s => s.Categoria == "inventario");
            var rolTieneAcceso = RolesConInventario.Contains(rol);

            // Bicondicional: tieneInventario ↔ rolTieneAcceso
            var bicondicional = tieneInventario == rolTieneAcceso;

            return bicondicional.ToProperty()
                .Label($"Bicondicional violado para rol '{rol}': tieneInventario={tieneInventario}, rolTieneAcceso={rolTieneAcceso}");
        });
    }

    #endregion
}
