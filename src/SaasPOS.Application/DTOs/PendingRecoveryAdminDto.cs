namespace SaasPOS.Application.DTOs;

/// <summary>
/// Variante cross-tenant de <see cref="PendingRecoveryDto"/> usada por el panel Admin
/// (Requirement 13). Además de los datos de la solicitud, incluye el comercio al que
/// pertenece el solicitante para poder mostrarlo en la columna del modal de dueños.
/// </summary>
public class PendingRecoveryAdminDto : PendingRecoveryDto
{
    /// <summary>Identificador del comercio (tenant) del usuario solicitante.</summary>
    public int ComercioId { get; set; }

    /// <summary>Nombre del comercio del usuario solicitante, para mostrar en el listado Admin.</summary>
    public string ComercioNombre { get; set; } = string.Empty;
}
