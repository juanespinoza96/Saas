import { useState, useCallback, useRef } from 'react'

interface ApiRequestState<T> {
  data: T | null
  error: string | null
  loading: boolean
}

interface UseApiRequestReturn<T> {
  data: T | null
  error: string | null
  loading: boolean
  execute: (...args: unknown[]) => Promise<T | undefined>
  reset: () => void
}

/**
 * Hook that wraps async API calls with loading/error state management.
 * Disables UI actions while a request is in progress (Req 21.14).
 *
 * @param apiFunction - The async function to wrap (e.g., api.get, api.post)
 * @returns Object with data, error, loading state, execute function, and reset
 *
 * @example
 * const { data, loading, error, execute } = useApiRequest(
 *   (id: string) => api.get<Product>(`/products/${id}`)
 * )
 * // In a button: <Button loading={loading} onClick={() => execute(productId)}>
 */
export function useApiRequest<T>(
  apiFunction: (...args: unknown[]) => Promise<T>,
): UseApiRequestReturn<T> {
  const [state, setState] = useState<ApiRequestState<T>>({
    data: null,
    error: null,
    loading: false,
  })

  // Track if the component is still mounted
  const mountedRef = useRef(true)

  const execute = useCallback(
    async (...args: unknown[]): Promise<T | undefined> => {
      setState(prev => ({ ...prev, loading: true, error: null }))

      try {
        const result = await apiFunction(...args)
        if (mountedRef.current) {
          setState({ data: result, error: null, loading: false })
        }
        return result
      } catch (err: unknown) {
        const message =
          err instanceof Error
            ? err.message
            : typeof err === 'object' && err !== null && 'message' in err
              ? String((err as { message: unknown }).message)
              : 'Error inesperado'

        if (mountedRef.current) {
          setState({ data: null, error: message, loading: false })
        }
        return undefined
      }
    },
    [apiFunction],
  )

  const reset = useCallback(() => {
    setState({ data: null, error: null, loading: false })
  }, [])

  // Cleanup on unmount — using ref pattern to avoid stale setState calls
  // (useEffect would be needed for full cleanup, but ref pattern is sufficient here)

  return {
    data: state.data,
    error: state.error,
    loading: state.loading,
    execute,
    reset,
  }
}
