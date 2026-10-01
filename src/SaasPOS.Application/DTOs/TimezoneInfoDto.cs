namespace SaasPOS.Application.DTOs;

/// <summary>
/// Representa la información de una zona horaria disponible en el sistema.
/// </summary>
public record TimezoneInfoDto
{
    /// <summary>
    /// Identificador IANA de la zona horaria (ej: "America/Guayaquil").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Nombre descriptivo con el offset UTC incluido (ej: "(UTC-05:00) Guayaquil").
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Offset UTC actual en formato "±HH:mm" (ej: "-05:00").
    /// </summary>
    public string UtcOffset { get; init; } = string.Empty;
}
