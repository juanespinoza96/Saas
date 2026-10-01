namespace SaasPOS.Application.DTOs;

public record CategoriaDto(
    int Id,
    string Nombre);

public record CreateCategoriaRequest(
    string Nombre);

public record UpdateCategoriaRequest(
    string Nombre);
