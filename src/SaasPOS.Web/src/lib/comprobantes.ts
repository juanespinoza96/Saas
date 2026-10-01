/**
 * Módulo de lógica pura para tipos de comprobante.
 * Contiene las constantes y funciones de filtrado utilizadas
 * tanto en la configuración como en el flujo de venta del POS.
 */

/** Tipos de comprobante soportados por el sistema */
export const TIPOS_COMPROBANTE = ['Ticket Digital', 'Ticket Impreso', 'Factura Electrónica'] as const

export type TipoComprobante = typeof TIPOS_COMPROBANTE[number]

/** DTO de configuración de comprobante recibido del backend */
export interface ConfiguracionComprobanteDto {
  tipoComprobante: string
  habilitado: boolean
}

/**
 * Filtra la configuración de comprobantes y devuelve solo los tipos habilitados.
 * Esta función implementa el Req 6.5: el selector de tipo de comprobante
 * muestra exclusivamente los tipos habilitados en la configuración del comercio.
 *
 * @param configuracion - Lista de configuraciones de comprobante del comercio
 * @returns Lista de nombres de tipos de comprobante que están habilitados
 */
export function obtenerTiposHabilitados(configuracion: ConfiguracionComprobanteDto[]): string[] {
  return configuracion
    .filter((c) => c.habilitado)
    .map((c) => c.tipoComprobante)
}
