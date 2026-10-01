namespace SaasPOS.Application.DTOs;

/// <summary>
/// Representa un rol disponible para asignación en un comercio.
/// </summary>
/// <param name="Id">Identificador del rol.</param>
/// <param name="Nombre">Nombre legible del rol.</param>
public record RolDisponibleDto(string Id, string Nombre);
