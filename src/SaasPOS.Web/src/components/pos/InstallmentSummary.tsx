/**
 * Componente de resumen visual de cuotas para pagos diferidos con tarjeta de crédito.
 * Muestra el total, número de cuotas y valor por cuota calculado en tiempo real.
 * Solo se renderiza cuando cuotas > 0 (pago diferido).
 *
 * Validates: Requirements 1.5, 2.1, 2.2, 2.3, 2.4
 */

/** Interfaz de props del componente InstallmentSummary */
export interface InstallmentSummaryProps {
  /** Monto total de la venta */
  total: number
  /** Número de cuotas seleccionadas (0 = corriente/pago completo) */
  cuotas: number
}

/**
 * Calcula el valor de cada cuota dividiendo el total entre el número de cuotas,
 * redondeado a 2 decimales.
 *
 * @param total - Monto total de la venta
 * @param cuotas - Número de cuotas (debe ser > 0)
 * @returns Valor por cuota redondeado a 2 decimales
 */
export function calcularValorCuota(total: number, cuotas: number): number {
  if (cuotas <= 0) return 0
  return Math.round((total / cuotas) * 100) / 100
}

/**
 * Formatea un número como moneda con símbolo $ y 2 decimales.
 *
 * @param valor - Número a formatear
 * @returns Cadena formateada como "$X.XX"
 */
export function formatearMoneda(valor: number): string {
  return `$${valor.toFixed(2)}`
}

/**
 * Formatea un valor de cuota mensual con formato de moneda y etiqueta "por mes".
 *
 * @param valor - Valor de la cuota mensual
 * @returns Cadena formateada como "$X.XX por mes"
 */
export function formatearCuotaMensual(valor: number): string {
  return `${formatearMoneda(valor)} por mes`
}

/**
 * Componente presentacional que muestra el resumen de cuotas.
 * Se renderiza solo cuando cuotas > 0 (pago diferido).
 * Recalcula inmediatamente al cambiar las props sin recarga de página.
 */
export function InstallmentSummary({ total, cuotas }: InstallmentSummaryProps) {
  // No renderizar si no es pago diferido (cuotas = 0 significa "Corriente")
  if (cuotas <= 0) return null

  const valorCuota = calcularValorCuota(total, cuotas)

  return (
    <section
      aria-label="Resumen de cuotas"
      className="rounded-lg border border-gray-200 bg-white p-4 dark:border-gray-700 dark:bg-dark-surface"
    >
      {/* Título del resumen */}
      <h3 className="mb-3 text-sm font-semibold text-gray-700 dark:text-dark-text">
        Resumen de Pago Diferido
      </h3>

      {/* Detalle de cuotas */}
      <dl className="space-y-2 text-sm">
        {/* Total de la venta */}
        <div className="flex items-center justify-between">
          <dt className="text-gray-600 dark:text-gray-400">Total de la venta</dt>
          <dd className="font-medium text-gray-900 dark:text-dark-text">
            {formatearMoneda(total)}
          </dd>
        </div>

        {/* Número de cuotas */}
        <div className="flex items-center justify-between">
          <dt className="text-gray-600 dark:text-gray-400">Cuotas</dt>
          <dd className="font-medium text-gray-900 dark:text-dark-text">
            {cuotas} meses
          </dd>
        </div>

        {/* Valor por cuota (destacado) */}
        <div className="flex items-center justify-between border-t border-gray-100 pt-2 dark:border-gray-700">
          <dt className="font-medium text-gray-700 dark:text-gray-300">
            Valor por cuota
          </dt>
          <dd className="text-base font-bold text-action-confirm">
            {formatearMoneda(valorCuota)}{' '}
            <span className="text-xs font-normal text-gray-500 dark:text-gray-400">
              por mes
            </span>
          </dd>
        </div>
      </dl>

      {/* Nota informativa sobre procesamiento externo (Req 2.4) */}
      <p
        role="note"
        className="mt-3 rounded bg-gray-50 p-2 text-xs text-gray-500 dark:bg-dark-bg dark:text-gray-400"
      >
        Este sistema solo registra la información de pago. El procesamiento real se
        realiza en el terminal externo.
      </p>
    </section>
  )
}
