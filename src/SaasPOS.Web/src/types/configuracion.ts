export interface ConfiguracionSucursalResponse {
  sucursalId: number
  esBarEscolar: boolean
  mostrarBotonCliente: boolean
  permiteVentaEnNegativo: boolean
  impresionAutomaticaTicket: boolean
  permitePrecioNegociado: boolean
  mostrarVentasAlCajero: boolean
  planNivel: string
  usaFacturacionSRI: boolean
}

export interface UpdateConfiguracionRequest {
  esBarEscolar: boolean
  mostrarBotonCliente: boolean
  permiteVentaEnNegativo: boolean
  impresionAutomaticaTicket: boolean
  mostrarVentasAlCajero: boolean
}

export interface Sucursal {
  id: number
  nombre: string
  direccion: string | null
  telefono: string | null
  serieFacturacion: string | null
}
