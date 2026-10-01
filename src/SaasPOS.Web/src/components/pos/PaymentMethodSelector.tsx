// Selector de método de pago para el POS
// Soporta: Efectivo, Tarjeta de Crédito, Tarjeta de Débito, Transferencia
// Muestra selector de cuotas solo para Tarjeta de Crédito (Req 1.3, 1.4)
// Muestra campo de referencia para métodos no-efectivo (Req 1.6, 1.8)

/** Tipos de método de pago disponibles en el sistema */
export type MetodoPago = 'Efectivo' | 'TarjetaCredito' | 'TarjetaDebito' | 'Transferencia'

/** Valores válidos para cuotas de tarjeta de crédito */
export type CuotasValidas = 0 | 3 | 6 | 9 | 12 | 18

export interface PaymentMethodSelectorProps {
  /** Método de pago seleccionado actualmente */
  metodoPago: MetodoPago
  /** Número de cuotas seleccionado (0 = Corriente) */
  cuotas: CuotasValidas
  /** Referencia de transacción */
  referencia: string
  /** Callback cuando cambia el método de pago */
  onMetodoPagoChange: (metodo: MetodoPago) => void
  /** Callback cuando cambia el número de cuotas */
  onCuotasChange: (cuotas: CuotasValidas) => void
  /** Callback cuando cambia la referencia */
  onReferenciaChange: (referencia: string) => void
}

/** Opciones de métodos de pago disponibles */
const METODOS_PAGO: { value: MetodoPago; label: string }[] = [
  { value: 'Efectivo', label: 'Efectivo' },
  { value: 'TarjetaCredito', label: 'Tarjeta de Crédito' },
  { value: 'TarjetaDebito', label: 'Tarjeta de Débito' },
  { value: 'Transferencia', label: 'Transferencia' },
]

/** Opciones de cuotas disponibles para tarjeta de crédito */
const OPCIONES_CUOTAS: { value: CuotasValidas; label: string }[] = [
  { value: 0, label: 'Corriente' },
  { value: 3, label: '3 meses' },
  { value: 6, label: '6 meses' },
  { value: 9, label: '9 meses' },
  { value: 12, label: '12 meses' },
  { value: 18, label: '18 meses' },
]

/**
 * Componente selector de método de pago.
 * Renderiza un dropdown para seleccionar el método, un selector de cuotas
 * condicional (solo Tarjeta de Crédito) y un campo de referencia condicional.
 *
 * Requisitos: 1.1, 1.2, 1.3, 1.4, 1.6, 1.8
 */
export function PaymentMethodSelector({
  metodoPago,
  cuotas,
  referencia,
  onMetodoPagoChange,
  onCuotasChange,
  onReferenciaChange,
}: PaymentMethodSelectorProps) {
  // Determinar si el campo de referencia debe estar habilitado (Req 1.6, 1.8)
  const referenciaHabilitada = metodoPago !== 'Efectivo'

  // Determinar si mostrar selector de cuotas (Req 1.3, 1.4)
  const mostrarCuotas = metodoPago === 'TarjetaCredito'

  return (
    <div className="space-y-3">
      {/* Selector de método de pago (Req 1.1) */}
      <div>
        <label
          htmlFor="metodo-pago-select"
          className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1"
        >
          Método de Pago
        </label>
        <select
          id="metodo-pago-select"
          value={metodoPago}
          onChange={(e) => {
            const nuevoMetodo = e.target.value as MetodoPago
            onMetodoPagoChange(nuevoMetodo)
            // Resetear cuotas si se cambia a un método que no sea Tarjeta de Crédito
            if (nuevoMetodo !== 'TarjetaCredito') {
              onCuotasChange(0)
            }
          }}
          className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm focus:outline-none focus:ring-2 focus:ring-action-confirm"
          aria-label="Método de pago"
        >
          {METODOS_PAGO.map((metodo) => (
            <option key={metodo.value} value={metodo.value}>
              {metodo.label}
            </option>
          ))}
        </select>
      </div>

      {/* Selector de cuotas - solo visible para Tarjeta de Crédito (Req 1.3, 1.4) */}
      {mostrarCuotas && (
        <div>
          <label
            htmlFor="cuotas-select"
            className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1"
          >
            Cuotas
          </label>
          <select
            id="cuotas-select"
            value={cuotas}
            onChange={(e) => onCuotasChange(Number(e.target.value) as CuotasValidas)}
            className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm focus:outline-none focus:ring-2 focus:ring-action-confirm"
            aria-label="Número de cuotas"
          >
            {OPCIONES_CUOTAS.map((opcion) => (
              <option key={opcion.value} value={opcion.value}>
                {opcion.label}
              </option>
            ))}
          </select>
        </div>
      )}

      {/* Campo de referencia de transacción (Req 1.6, 1.8) */}
      <div>
        <label
          htmlFor="referencia-input"
          className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1"
        >
          Referencia de Transacción
        </label>
        <input
          id="referencia-input"
          type="text"
          value={referencia}
          onChange={(e) => onReferenciaChange(e.target.value)}
          disabled={!referenciaHabilitada}
          placeholder={referenciaHabilitada ? 'Número de referencia (opcional)' : 'No aplica para efectivo'}
          className={`w-full px-3 py-2 rounded-lg border text-sm focus:outline-none focus:ring-2 focus:ring-action-confirm
            ${referenciaHabilitada
              ? 'border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text placeholder-gray-400'
              : 'border-gray-200 dark:border-gray-700 bg-gray-100 dark:bg-dark-bg/50 text-gray-400 dark:text-gray-500 cursor-not-allowed'
            }`}
          aria-label="Referencia de transacción"
          aria-disabled={!referenciaHabilitada}
        />
      </div>
    </div>
  )
}
