namespace SaasPOS.Application.DTOs;

public record ProductoDto(
    int Id,
    string Nombre,
    string TipoArticulo,
    int? CategoriaId,
    string? ValoresDinamicos,
    bool ManejaStock,
    string? UnidadMedida,
    decimal CostoProduccion,
    decimal PrecioLista,
    decimal PrecioMinimo);

public record CreateProductoRequest(
    string Nombre,
    string TipoArticulo,
    int? CategoriaId,
    string? ValoresDinamicos,
    bool ManejaStock,
    string? UnidadMedida,
    decimal CostoProduccion,
    decimal PrecioLista,
    decimal PrecioMinimo);

public record UpdateProductoRequest(
    string Nombre,
    string TipoArticulo,
    int? CategoriaId,
    string? ValoresDinamicos,
    bool ManejaStock,
    string? UnidadMedida,
    decimal CostoProduccion,
    decimal PrecioLista,
    decimal PrecioMinimo);
