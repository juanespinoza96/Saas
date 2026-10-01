namespace SaasPOS.Application.DTOs;

public record ComercioDto(
    int Id,
    string Ruc,
    string RazonSocial,
    int PlanId,
    string PlanNombre,
    string Estado,
    bool UsaFacturacionSRI,
    DateTime FechaRegistro);

public record CreateComercioRequest(
    string Ruc,
    string RazonSocial,
    int PlanId,
    bool UsaFacturacionSRI = false);

public record UpdateComercioRequest(
    string RazonSocial,
    int PlanId,
    bool UsaFacturacionSRI);
