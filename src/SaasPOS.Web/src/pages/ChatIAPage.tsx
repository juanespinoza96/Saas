import { useState, useRef, useEffect } from 'react'
import { api, type ApiError } from '../lib/api'
import { ChatMessage as ChatMessageComponent } from '../components/chat/ChatMessage'

// DTOs para el chat de BI
interface ChatMessageDto {
  role: 'system' | 'user' | 'assistant'
  content: string
}

interface ChatBIResponse {
  texto: string
  grafico?: ChartData
  tokensUsados: number
}

interface ChartData {
  tipo: string
  labels: string[]
  valores: number[]
  titulo?: string
}

interface SugerenciaChat {
  texto: string
  categoria: string
}

// Estructura interna para almacenar mensajes con su gráfico asociado
interface MensajeConGrafico {
  mensaje: ChatMessageDto
  grafico?: ChartData
}

type PlanNivel = 'Básico' | 'Intermedio' | 'Empresarial'

interface ConfiguracionPlan {
  planNivel: PlanNivel
}

/**
 * Página de Chat IA - Inteligencia de Negocios.
 * Permite al usuario hacer preguntas sobre su comercio y recibir respuestas con contexto acumulativo.
 * Incluye gating por plan (solo Plan Empresarial), sugerencias contextuales y manejo de errores.
 * Requisitos: 7.1, 7.2, 7.3, 7.4, 7.7, 7.9, 7.11
 */
export function ChatIAPage() {
  // Estado del plan para gating
  const [planNivel, setPlanNivel] = useState<PlanNivel | null>(null)
  const [loadingPlan, setLoadingPlan] = useState(true)

  // Sugerencias contextuales cargadas del backend
  const [sugerencias, setSugerencias] = useState<SugerenciaChat[]>([])
  const [loadingSugerencias, setLoadingSugerencias] = useState(false)

  // Historial de mensajes con gráficos asociados de la sesión activa
  const [mensajes, setMensajes] = useState<MensajeConGrafico[]>([])
  // Texto actual del input
  const [input, setInput] = useState('')
  // Estado de carga mientras se espera respuesta de la IA
  const [loading, setLoading] = useState(false)
  // Mensaje de error para mostrar al usuario
  const [error, setError] = useState<string | null>(null)

  // Referencia al contenedor de mensajes para auto-scroll
  const mensajesEndRef = useRef<HTMLDivElement>(null)

  // Cargar plan del comercio para gating (Req 7.11)
  useEffect(() => {
    async function fetchPlan() {
      try {
        const config = await api.get<ConfiguracionPlan>('/api/tenants/configuracion')
        setPlanNivel(config.planNivel)
      } catch {
        setPlanNivel('Básico')
      } finally {
        setLoadingPlan(false)
      }
    }
    fetchPlan()
  }, [])

  // Cargar sugerencias contextuales del endpoint (Req 7.2, 7.3, 7.4)
  useEffect(() => {
    if (planNivel !== 'Empresarial') return

    async function fetchSugerencias() {
      setLoadingSugerencias(true)
      try {
        const data = await api.get<SugerenciaChat[]>('/api/tenants/ai/chat-bi/sugerencias')
        setSugerencias(data)
      } catch {
        // Si falla la carga de sugerencias, no se muestran pero no se bloquea la UI
        setSugerencias([])
      } finally {
        setLoadingSugerencias(false)
      }
    }
    fetchSugerencias()
  }, [planNivel])

  // Auto-scroll al final cuando se agregan nuevos mensajes
  useEffect(() => {
    mensajesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [mensajes])

  /**
   * Envía la pregunta del usuario al endpoint de chat BI.
   * Maneja errores específicos: 429 (rate limit) y 503 (servicio no disponible).
   */
  async function handleEnviar(preguntaDirecta?: string) {
    const pregunta = (preguntaDirecta ?? input).trim()
    if (!pregunta || loading) return

    setError(null)
    setInput('')

    // Agregar mensaje del usuario al historial
    const mensajeUsuario: ChatMessageDto = { role: 'user', content: pregunta }
    const historialActualizado = [...mensajes.map(m => m.mensaje), mensajeUsuario]
    setMensajes(prev => [...prev, { mensaje: mensajeUsuario }])

    setLoading(true)
    try {
      const response = await api.post<ChatBIResponse>('/api/tenants/ai/chat-bi', {
        pregunta,
        historial: historialActualizado,
      })

      // Agregar respuesta del asistente al historial con su gráfico asociado
      const mensajeAsistente: ChatMessageDto = {
        role: 'assistant',
        content: response.texto,
      }
      setMensajes(prev => [...prev, { mensaje: mensajeAsistente, grafico: response.grafico }])
    } catch (err: unknown) {
      // Manejo de errores específicos (Req 7.9)
      const apiError = err as ApiError
      if (apiError?.status === 429) {
        setError('Has alcanzado el límite de 20 consultas por hora. Por favor intenta más tarde.')
      } else if (apiError?.status === 503) {
        setError('El servicio de IA no está disponible temporalmente. Por favor intenta más tarde.')
      } else {
        const details = apiError?.details as { message?: string } | undefined
        const mensajeError = details?.message || apiError?.message || 'Error al comunicarse con el asistente IA. Por favor intenta más tarde.'
        setError(mensajeError)
      }
    } finally {
      setLoading(false)
    }
  }

  /**
   * Permite enviar con Enter (sin Shift para nueva línea).
   */
  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleEnviar()
    }
  }

  /**
   * Maneja el clic en una sugerencia: envía la pregunta directamente.
   */
  function handleSugerenciaClick(texto: string) {
    setInput(texto)
    handleEnviar(texto)
  }

  // Estado de carga del plan
  if (loadingPlan) {
    return (
      <div className="flex items-center justify-center h-[calc(100vh-8rem)]">
        <div className="animate-spin h-8 w-8 border-4 border-gray-300 border-t-action-confirm rounded-full" />
      </div>
    )
  }

  // Banner promocional para planes sin acceso (Req 7.11)
  if (planNivel !== 'Empresarial') {
    return (
      <div className="flex flex-col items-center justify-center h-[calc(100vh-8rem)] px-4">
        <div className="max-w-lg w-full bg-white dark:bg-dark-surface rounded-xl shadow-lg p-8 text-center">
          {/* Ícono de IA */}
          <div className="mx-auto w-16 h-16 bg-purple-100 dark:bg-purple-900/30 rounded-full flex items-center justify-center mb-6">
            <svg
              className="h-8 w-8 text-purple-600 dark:text-purple-400"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              xmlns="http://www.w3.org/2000/svg"
              aria-hidden="true"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.455 2.456L21.75 6l-1.036.259a3.375 3.375 0 00-2.455 2.456z"
              />
            </svg>
          </div>
          <h2 className="text-xl font-bold text-gray-900 dark:text-dark-text mb-3">
            Chat IA - Inteligencia de Negocios
          </h2>
          <p className="text-gray-600 dark:text-gray-400 mb-6">
            Obtén insights de tu negocio en lenguaje natural. Pregunta sobre ventas, tendencias,
            inventario y más con nuestro asistente de inteligencia artificial.
          </p>
          <div className="bg-purple-50 dark:bg-purple-900/20 border border-purple-200 dark:border-purple-800 rounded-lg p-4">
            <p className="text-sm font-medium text-purple-800 dark:text-purple-300">
              ✨ Esta funcionalidad está disponible en el Plan Empresarial
            </p>
            <p className="text-xs text-purple-600 dark:text-purple-400 mt-1">
              Actualiza tu plan para acceder al Chat IA y otras funcionalidades avanzadas.
            </p>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col h-[calc(100vh-8rem)]">
      {/* Encabezado */}
      <div className="mb-4">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">
          Chat IA - Inteligencia de Negocios
        </h1>
        <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
          Pregunta sobre ventas, inventario, tendencias y más de tu comercio.
        </p>
      </div>

      {/* Área de mensajes con scroll */}
      <div className="flex-1 overflow-y-auto bg-white dark:bg-dark-surface rounded-lg shadow p-4 space-y-4">
        {mensajes.length === 0 && !loading && (
          <div className="flex flex-col items-center justify-center h-full text-center">
            <svg
              className="h-12 w-12 text-gray-400 mb-4"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z"
              />
            </svg>
            <p className="text-gray-500 dark:text-gray-400 mb-4">
              Escribe una pregunta o selecciona una sugerencia para comenzar.
            </p>

            {/* Sugerencias contextuales (Req 7.2, 7.3, 7.4) */}
            {loadingSugerencias ? (
              <div className="flex items-center gap-2 text-sm text-gray-400">
                <div className="animate-spin h-4 w-4 border-2 border-gray-300 border-t-transparent rounded-full" />
                <span>Cargando sugerencias...</span>
              </div>
            ) : sugerencias.length > 0 ? (
              <div className="w-full max-w-2xl">
                <p className="text-xs text-gray-400 dark:text-gray-500 mb-3 uppercase tracking-wide">
                  Preguntas sugeridas
                </p>
                <div className="flex flex-wrap justify-center gap-2">
                  {sugerencias.map((sugerencia, idx) => (
                    <button
                      key={idx}
                      onClick={() => handleSugerenciaClick(sugerencia.texto)}
                      className="px-4 py-2 text-sm bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded-full hover:bg-purple-100 dark:hover:bg-purple-900/30 hover:text-purple-700 dark:hover:text-purple-300 transition-colors border border-gray-200 dark:border-gray-600 hover:border-purple-300 dark:hover:border-purple-700"
                      title={sugerencia.categoria}
                    >
                      {sugerencia.texto}
                    </button>
                  ))}
                </div>
              </div>
            ) : (
              <p className="text-xs text-gray-400 dark:text-gray-500 mt-2">
                Ejemplos: &quot;¿Cuáles fueron mis ventas de hoy?&quot;, &quot;¿Qué producto se vendió más esta semana?&quot;
              </p>
            )}
          </div>
        )}

        {mensajes.map((item, index) => (
          <ChatMessageComponent
            key={index}
            message={item.mensaje as { role: 'user' | 'assistant'; content: string }}
            grafico={item.grafico}
          />
        ))}

        {/* Indicador de carga */}
        {loading && (
          <div className="flex justify-start">
            <div className="bg-gray-100 dark:bg-gray-700 rounded-lg px-4 py-3">
              <div className="flex items-center gap-2">
                <div className="animate-spin h-4 w-4 border-2 border-gray-400 border-t-transparent rounded-full" />
                <span className="text-sm text-gray-500 dark:text-gray-400">
                  Pensando...
                </span>
              </div>
            </div>
          </div>
        )}

        {/* Referencia para auto-scroll */}
        <div ref={mensajesEndRef} />
      </div>

      {/* Mensaje de error (Req 7.9) */}
      {error && (
        <div className="mt-2 p-3 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
          <p className="text-sm text-red-600 dark:text-red-400">{error}</p>
        </div>
      )}

      {/* Input de texto y botón enviar */}
      <div className="mt-4 flex gap-3">
        <textarea
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Escribe tu pregunta..."
          disabled={loading}
          rows={1}
          className="flex-1 resize-none rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-surface px-4 py-3 text-sm text-gray-900 dark:text-dark-text placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-action-confirm focus:border-transparent disabled:opacity-50"
        />
        <button
          onClick={() => handleEnviar()}
          disabled={!input.trim() || loading}
          className="px-6 py-3 bg-action-confirm hover:bg-action-confirm/90 text-white font-medium rounded-lg text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          Enviar
        </button>
      </div>
    </div>
  )
}
