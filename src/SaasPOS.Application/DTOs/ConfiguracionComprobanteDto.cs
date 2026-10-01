namespace SaasPOS.Application.DTOs;

/// <summary>
/// Representa la configuración de un tipo de comprobante para un comercio.
/// </summary>
public class ConfiguracionComprobanteDto
{
    /// <summary>
    /// Tipo de comprobante: "Ticket Digital", "Ticket Impreso" o "Factura Electrónica".
    /// </summary>
    public string TipoComprobante { get; set; } = string.Empty;

    /// <summary>
    /// Indica si el tipo de comprobante está habilitado para el comercio.
    /// </summary>
    public bool Habilitado { get; set; }
}

/// <summary>
/// Request para actualizar la configuración de tipos de comprobante habilitados.
/// </summary>
public class UpdateComprobantesRequest
{
    /// <summary>
    /// Lista de tipos de comprobante con su estado de habilitación.
    /// Debe contener al menos un tipo habilitado.
    /// </summary>
    public List<ConfiguracionComprobanteDto> Tipos { get; set; } = [];
}
