// Componente de impresión de ticket: solo visible al imprimir (print:block)

interface TicketPrintProps {
  comercioNombre: string
  comercioRuc?: string
  cliente?: { nombre: string; identificacion: string } | null
  items: { nombre: string; cantidad: number; precioUnitario: number; subtotal: number }[]
  total: number
  tipoComprobante: string
  metodoPago: string
  fecha: Date
}

function formatFecha(fecha: Date): string {
  return fecha.toLocaleString('es-EC', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function formatMetodoPago(metodo: string): string {
  switch (metodo) {
    case 'TarjetaCredito':
      return 'Tarjeta de Crédito'
    case 'TarjetaDebito':
      return 'Tarjeta de Débito'
    default:
      return metodo
  }
}

export function TicketPrint({
  comercioNombre,
  comercioRuc,
  cliente,
  items,
  total,
  tipoComprobante,
  metodoPago,
  fecha,
}: TicketPrintProps) {
  return (
    <div
      id="ticket-print-root"
      className="hidden print:block font-mono text-xs w-[80mm] mx-auto p-2"
    >
      {/* Encabezado del comercio */}
      <div className="text-center mb-2">
        <p className="font-bold text-sm">{comercioNombre}</p>
        {comercioRuc && <p>RUC: {comercioRuc}</p>}
      </div>

      {/* Separador */}
      <p className="text-center">{'─'.repeat(32)}</p>

      {/* Información del cliente */}
      <div className="my-1">
        {cliente ? (
          <>
            <p>Cliente: {cliente.nombre}</p>
            <p>CI/RUC: {cliente.identificacion}</p>
          </>
        ) : (
          <p>CONSUMIDOR FINAL</p>
        )}
      </div>

      {/* Separador */}
      <p className="text-center">{'─'.repeat(32)}</p>

      {/* Tabla de items */}
      <table className="w-full my-1">
        <thead>
          <tr className="text-left">
            <th className="pr-1">Cant</th>
            <th>Descripción</th>
            <th className="text-right pr-1">P.Unit</th>
            <th className="text-right">Subtotal</th>
          </tr>
        </thead>
        <tbody>
          {items.map((item, index) => (
            <tr key={index}>
              <td className="pr-1 align-top">{item.cantidad}</td>
              <td className="align-top">{item.nombre}</td>
              <td className="text-right pr-1 align-top">
                {item.precioUnitario.toFixed(2)}
              </td>
              <td className="text-right align-top">
                {item.subtotal.toFixed(2)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* Separador */}
      <p className="text-center">{'─'.repeat(32)}</p>

      {/* Total */}
      <div className="flex justify-between font-bold text-sm my-2">
        <span>TOTAL:</span>
        <span>${total.toFixed(2)}</span>
      </div>

      {/* Separador */}
      <p className="text-center">{'─'.repeat(32)}</p>

      {/* Pie: comprobante, método, fecha */}
      <div className="mt-1 space-y-0.5">
        <p>Comprobante: {tipoComprobante}</p>
        <p>Método de Pago: {formatMetodoPago(metodoPago)}</p>
        <p>Fecha: {formatFecha(fecha)}</p>
      </div>

      {/* Mensaje final */}
      <p className="text-center mt-3 font-bold">¡Gracias por su compra!</p>
    </div>
  )
}
