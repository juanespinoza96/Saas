namespace SaasPOS.Application.DTOs;

public record ConfiguracionSucursalDto(
    int SucursalId,
    bool EsBarEscolar,
    bool MostrarBotonCliente,
    bool PermiteVentaEnNegativo,
    bool ImpresionAutomaticaTicket,
    bool PermitePrecioNegociado,
    bool MostrarVentasAlCajero);

public record ConfiguracionSucursalResponseDto(
    int SucursalId,
    bool EsBarEscolar,
    bool MostrarBotonCliente,
    bool PermiteVentaEnNegativo,
    bool ImpresionAutomaticaTicket,
    bool PermitePrecioNegociado,
    bool MostrarVentasAlCajero,
    string PlanNivel,
    bool UsaFacturacionSRI);

public record UpdateConfiguracionSucursalRequest(
    bool EsBarEscolar,
    bool MostrarBotonCliente,
    bool PermiteVentaEnNegativo,
    bool ImpresionAutomaticaTicket,
    bool PermitePrecioNegociado,
    bool MostrarVentasAlCajero);
