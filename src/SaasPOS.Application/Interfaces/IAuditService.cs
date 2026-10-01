namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Registers audit entries in LogsAuditoria for tracking entity changes.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Registers an audit log entry.
    /// </summary>
    /// <param name="comercioId">The comercio that owns the record.</param>
    /// <param name="usuarioId">The user performing the action (nullable).</param>
    /// <param name="accion">Action performed (e.g., "Crear", "Actualizar").</param>
    /// <param name="tablaAfectada">Table name affected.</param>
    /// <param name="registroId">Primary key of the affected record.</param>
    /// <param name="valoresAnteriores">Previous values (null for creation).</param>
    /// <param name="valoresNuevos">New values (null for deletion).</param>
    Task RegistrarAsync(int comercioId, int? usuarioId, string accion, string tablaAfectada, string registroId, object? valoresAnteriores, object? valoresNuevos);
}
