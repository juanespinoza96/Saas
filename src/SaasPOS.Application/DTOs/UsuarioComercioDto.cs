namespace SaasPOS.Application.DTOs;

/// <summary>
/// Usuario de un comercio visto desde el panel SuperAdmin (GET /api/admin/comercios/{id}/usuarios).
/// Coincide con el tipo `Usuario` de ComercioDetallePage.tsx. Nunca incluye el hash de contraseña
/// ni otros datos sensibles.
/// </summary>
public record UsuarioComercioDto(
    int Id,
    string Nombre,
    string Email,
    string Rol,
    bool Activo);
