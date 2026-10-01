namespace SaasPOS.Application.DTOs;

public record PrecioVolumenDto(
    int Id,
    int ProductoId,
    decimal CantidadMinima,
    decimal PrecioEspecial);

public record CreatePrecioVolumenRequest(
    decimal CantidadMinima,
    decimal PrecioEspecial);
