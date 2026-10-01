using SaasPOS.Application.DTOs;

namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Servicio de gestión de tipos de comprobante habilitados por comercio.
/// Valida invariantes de negocio: mínimo 1 tipo habilitado y prerrequisitos para Factura Electrónica.
/// </summary>
public interface IComprobantesConfigService
{
    /// <summary>
    /// Obtiene la lista de tipos de comprobante con su estado de habilitación para un comercio.
    /// </summary>
    Task<List<ConfiguracionComprobanteDto>> GetHabilitadosAsync(int comercioId);

    /// <summary>
    /// Habilita un tipo de comprobante para el comercio.
    /// Valida prerrequisitos para Factura Electrónica (plan + firma SRI).
    /// </summary>
    Task<Result> HabilitarTipoAsync(int comercioId, string tipo);

    /// <summary>
    /// Deshabilita un tipo de comprobante para el comercio.
    /// Rechaza la operación si resultaría en 0 tipos habilitados.
    /// </summary>
    Task<Result> DeshabilitarTipoAsync(int comercioId, string tipo);

    /// <summary>
    /// Verifica si un tipo de comprobante está habilitado para el comercio.
    /// </summary>
    Task<bool> EsTipoHabilitadoAsync(int comercioId, string tipo);

    /// <summary>
    /// Normaliza valores legacy de TipoComprobante:
    /// "Ticket Interno" → "Ticket Digital", "Factura Electronica" → "Factura Electrónica".
    /// Otros valores pasan sin cambios.
    /// </summary>
    string NormalizarTipoComprobante(string tipo);
}
