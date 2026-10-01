namespace SaasPOS.Application.DTOs;

public record UsuarioDto(int Id, string Nombre, string Email, string Rol, int? SucursalId, bool Activo);
public record CreateUsuarioRequest(string Nombre, string Email, string Password, string Rol, int? SucursalId);
public record UpdateUsuarioRequest(string Nombre, string Email, string Rol, int? SucursalId);

/// <summary>DTO para actualizar la zona horaria preferida de un usuario.</summary>
public record UpdateZonaHorariaRequest(string ZonaHoraria);
