namespace SaasPOS.Application.DTOs;

public record AtributoCategoriaDto(
    int Id,
    int CategoriaId,
    string NombreAtributo,
    string TipoDato);

public record CreateAtributoRequest(
    string NombreAtributo,
    string TipoDato);
