using SaasPOS.Application.DTOs;

namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Gestiona operaciones de prueba gratuita: creación, conversión y extensión de trials.
/// </summary>
public interface ITrialService
{
    /// <summary>
    /// Crea un comercio con suscripción trial de 15 días asignando Plan Básico.
    /// </summary>
    Task<TrialResultDto> CrearTrialAsync(CreateTrialRequest request, int superAdminId);

    /// <summary>
    /// Convierte un trial (activo o expirado) a una suscripción paga con el plan seleccionado.
    /// </summary>
    Task<ConversionResultDto> ConvertirTrialAsync(int suscripcionId, ConvertTrialRequest request, int superAdminId);

    /// <summary>
    /// Extiende la FechaProximoCorte de un trial activo por los días indicados.
    /// </summary>
    Task<ExtensionResultDto> ExtenderTrialAsync(int suscripcionId, ExtendTrialRequest request, int superAdminId);

    /// <summary>
    /// Obtiene listado de trials paginado, ordenado por días restantes ascendente.
    /// </summary>
    Task<List<TrialDto>> ObtenerTrialsAsync();

    /// <summary>
    /// Obtiene resumen de contadores de trials.
    /// </summary>
    Task<TrialResumenDto> ObtenerResumenAsync();

    /// <summary>
    /// Verifica si un RUC es elegible para trial (no tiene historial previo).
    /// </summary>
    Task<bool> EsElegibleParaTrialAsync(string ruc);
}
