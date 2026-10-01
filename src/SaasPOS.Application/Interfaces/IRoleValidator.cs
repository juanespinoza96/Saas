using SaasPOS.Application.DTOs;

namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Servicio de validación de roles por plan de suscripción.
/// Encapsula la lógica de verificación de roles permitidos, unicidad del Dueño
/// y exclusión de SuperAdmin.
/// </summary>
public interface IRoleValidator
{
    /// <summary>
    /// Retorna los roles asignables para el plan del comercio dado.
    /// Excluye SuperAdmin siempre. Excluye Dueño si ya existe uno.
    /// </summary>
    Task<IReadOnlyList<RolDisponibleDto>> GetRolesDisponiblesAsync(int comercioId);

    /// <summary>
    /// Valida que el rol solicitado sea permitido por el plan del comercio.
    /// Retorna (esValido, mensajeError, rolesDisponibles).
    /// </summary>
    Task<RoleValidationResult> ValidarRolParaComercioAsync(int comercioId, string rol);

    /// <summary>
    /// Verifica que no se pueda eliminar o cambiar rol al único Dueño.
    /// Retorna true si el usuario es el único Dueño del comercio.
    /// </summary>
    Task<bool> EsUnicoDuenoAsync(int comercioId, int usuarioId);
}
