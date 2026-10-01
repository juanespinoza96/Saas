/**
 * Componente de renderizado de gráficos para respuestas de Chat IA.
 * Soporta barras horizontales, líneas y gráfico de pastel usando CSS puro y Tailwind.
 * Requisito: 7.6
 */

// Tipo de datos para los gráficos generados por la IA
interface ChartData {
  tipo: string // "bar" | "line" | "pie"
  labels: string[]
  valores: number[]
  titulo?: string
}

interface ChartRendererProps {
  data: ChartData
}

// Colores predefinidos para las secciones de gráficos
const CHART_COLORS = [
  '#4CAF7D', // verde
  '#F5A623', // amarillo
  '#E57373', // rojo
  '#64B5F6', // azul
  '#BA68C8', // morado
  '#4DB6AC', // teal
  '#FFB74D', // naranja
  '#A1887F', // café
]

/**
 * Renderiza un gráfico de barras horizontales.
 * Cada barra muestra la proporción relativa al valor máximo.
 */
function BarChart({ data }: { data: ChartData }) {
  const maxValor = Math.max(...data.valores, 1)

  return (
    <div className="space-y-2">
      {data.labels.map((label, index) => {
        const valor = data.valores[index] ?? 0
        const porcentaje = (valor / maxValor) * 100
        const color = CHART_COLORS[index % CHART_COLORS.length]

        return (
          <div key={index} className="flex items-center gap-2">
            {/* Etiqueta */}
            <span className="text-xs text-gray-600 dark:text-gray-400 w-24 truncate" title={label}>
              {label}
            </span>
            {/* Barra */}
            <div className="flex-1 bg-gray-200 dark:bg-gray-600 rounded-full h-5 relative">
              <div
                className="h-5 rounded-full transition-all duration-500"
                style={{ width: `${porcentaje}%`, backgroundColor: color }}
              />
            </div>
            {/* Valor numérico */}
            <span className="text-xs font-medium text-gray-700 dark:text-gray-300 w-16 text-right">
              {valor.toLocaleString()}
            </span>
          </div>
        )
      })}
    </div>
  )
}

/**
 * Renderiza un gráfico de línea simple usando CSS.
 * Muestra puntos de datos conectados visualmente con posicionamiento relativo.
 */
function LineChart({ data }: { data: ChartData }) {
  const maxValor = Math.max(...data.valores, 1)
  const minValor = Math.min(...data.valores, 0)
  const rango = maxValor - minValor || 1

  return (
    <div className="space-y-2">
      {/* Área del gráfico de línea */}
      <div className="relative h-40 bg-gray-50 dark:bg-gray-800 rounded border border-gray-200 dark:border-gray-600 p-2">
        <div className="flex items-end justify-between h-full gap-1">
          {data.valores.map((valor, index) => {
            const alturaPorcentaje = ((valor - minValor) / rango) * 100

            return (
              <div key={index} className="flex flex-col items-center flex-1 h-full justify-end">
                {/* Punto de datos */}
                <div
                  className="w-3 h-3 rounded-full bg-blue-500 border-2 border-white dark:border-gray-800 shadow-sm z-10"
                  style={{ marginBottom: `${alturaPorcentaje}%` }}
                  title={`${data.labels[index]}: ${valor}`}
                />
                {/* Barra de soporte visual */}
                <div
                  className="w-1 bg-blue-200 dark:bg-blue-800 rounded-t"
                  style={{ height: `${alturaPorcentaje}%` }}
                />
              </div>
            )
          })}
        </div>
      </div>
      {/* Etiquetas del eje X */}
      <div className="flex justify-between">
        {data.labels.map((label, index) => (
          <span
            key={index}
            className="text-[10px] text-gray-500 dark:text-gray-400 truncate text-center flex-1"
            title={label}
          >
            {label}
          </span>
        ))}
      </div>
    </div>
  )
}

/**
 * Renderiza un gráfico de pastel usando conic-gradient CSS.
 * Incluye una leyenda con colores y porcentajes.
 */
function PieChart({ data }: { data: ChartData }) {
  const total = data.valores.reduce((sum, v) => sum + v, 0) || 1

  // Construir el gradiente cónico con los segmentos
  let acumulado = 0
  const segmentos = data.valores.map((valor, index) => {
    const porcentaje = (valor / total) * 100
    const inicio = acumulado
    acumulado += porcentaje
    return {
      color: CHART_COLORS[index % CHART_COLORS.length],
      inicio,
      fin: acumulado,
      porcentaje,
      label: data.labels[index],
      valor,
    }
  })

  const gradiente = segmentos
    .map(s => `${s.color} ${s.inicio}% ${s.fin}%`)
    .join(', ')

  return (
    <div className="flex items-center gap-4">
      {/* Círculo del gráfico de pastel */}
      <div
        className="w-28 h-28 rounded-full shrink-0"
        style={{ background: `conic-gradient(${gradiente})` }}
        role="img"
        aria-label={`Gráfico de pastel: ${data.titulo || 'datos'}`}
      />
      {/* Leyenda */}
      <div className="space-y-1 overflow-y-auto max-h-32">
        {segmentos.map((s, index) => (
          <div key={index} className="flex items-center gap-2">
            <div
              className="w-3 h-3 rounded-sm shrink-0"
              style={{ backgroundColor: s.color }}
            />
            <span className="text-xs text-gray-600 dark:text-gray-400 truncate" title={s.label}>
              {s.label}
            </span>
            <span className="text-xs font-medium text-gray-700 dark:text-gray-300 ml-auto">
              {s.porcentaje.toFixed(1)}%
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

/**
 * Componente principal de renderizado de gráficos.
 * Determina el tipo de gráfico y delega al componente apropiado.
 */
export function ChartRenderer({ data }: ChartRendererProps) {
  // Validación básica de datos
  if (!data || !data.labels.length || !data.valores.length) {
    return null
  }

  return (
    <div className="mt-3 p-3 bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-600">
      {/* Título del gráfico */}
      {data.titulo && (
        <h4 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
          {data.titulo}
        </h4>
      )}

      {/* Renderizar según el tipo de gráfico */}
      {data.tipo === 'bar' && <BarChart data={data} />}
      {data.tipo === 'line' && <LineChart data={data} />}
      {data.tipo === 'pie' && <PieChart data={data} />}

      {/* Tipo no soportado */}
      {!['bar', 'line', 'pie'].includes(data.tipo) && (
        <p className="text-xs text-gray-400 italic">
          Tipo de gráfico no soportado: {data.tipo}
        </p>
      )}
    </div>
  )
}
