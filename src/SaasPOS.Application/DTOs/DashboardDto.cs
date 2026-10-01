namespace SaasPOS.Application.DTOs;

public record DashboardDto(
    List<IngresoPorPlanDto> IngresosPorPlan,
    decimal TotalMensualProyectado,
    List<ComercioEstadoDto> Comercios);

public record IngresoPorPlanDto(
    string PlanNombre,
    decimal TotalIngresos);

public record ComercioEstadoDto(
    int Id,
    string Ruc,
    string RazonSocial,
    string PlanNombre,
    string Estado,
    DateOnly? FechaProximoCorte);
