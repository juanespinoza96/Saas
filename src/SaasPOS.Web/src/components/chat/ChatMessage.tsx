/**
 * Componente de burbuja de mensaje individual para el Chat IA.
 * Sanitiza contenido de respuestas del asistente antes de renderizar.
 * Requisitos: 7.6, 7.10
 */

import { sanitizeHtml } from './sanitize'
import { ChartRenderer } from './ChartRenderer'

// Tipo de datos para los gráficos generados por la IA
interface ChartData {
  tipo: string // "bar" | "line" | "pie"
  labels: string[]
  valores: number[]
  titulo?: string
}

interface ChatMessageProps {
  /** Mensaje del chat con rol y contenido */
  message: {
    role: 'user' | 'assistant'
    content: string
  }
  /** Datos de gráfico opcionales (solo para respuestas del asistente) */
  grafico?: ChartData
}

/**
 * Renderiza una burbuja de mensaje individual del chat.
 * - Mensajes del usuario: alineados a la derecha con fondo verde.
 * - Mensajes del asistente: alineados a la izquierda con fondo gris y contenido sanitizado.
 * Si el mensaje del asistente incluye datos de gráfico, se renderiza debajo del texto.
 */
export function ChatMessage({ message, grafico }: ChatMessageProps) {
  const esUsuario = message.role === 'user'

  // Sanitizar contenido solo para mensajes del asistente (defensa contra XSS)
  const contenidoSeguro = esUsuario
    ? message.content
    : sanitizeHtml(message.content)

  return (
    <div className={`flex ${esUsuario ? 'justify-end' : 'justify-start'}`}>
      <div
        className={`max-w-[75%] rounded-lg px-4 py-3 ${
          esUsuario
            ? 'bg-action-confirm text-white'
            : 'bg-gray-100 dark:bg-gray-700 text-gray-900 dark:text-dark-text'
        }`}
      >
        {/* Contenido del mensaje */}
        <p className="text-sm whitespace-pre-wrap">{contenidoSeguro}</p>

        {/* Gráfico adjunto (solo para mensajes del asistente) */}
        {!esUsuario && grafico && <ChartRenderer data={grafico} />}
      </div>
    </div>
  )
}
