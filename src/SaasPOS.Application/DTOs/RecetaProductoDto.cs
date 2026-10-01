namespace SaasPOS.Application.DTOs;

public record RecetaProductoDto(
    int Id,
    int ProductoFinalId,
    int IngredienteId,
    decimal CantidadRequerida);

public record CreateRecetaRequest(
    int IngredienteId,
    decimal CantidadRequerida);
