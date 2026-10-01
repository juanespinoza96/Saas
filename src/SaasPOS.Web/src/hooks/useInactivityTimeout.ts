import { useEffect, useRef, useCallback } from 'react'

const INACTIVITY_TIMEOUT_MS = 30 * 60 * 1000 // 30 minutes (Req 3.3)

const ACTIVITY_EVENTS: (keyof DocumentEventMap)[] = [
  'mousedown',
  'mousemove',
  'keydown',
  'scroll',
  'touchstart',
  'click',
]

/**
 * Hook that monitors user activity and triggers a callback after inactivity.
 * Implements 30-minute session timeout (Req 3.3).
 *
 * @param onTimeout - Callback invoked when inactivity timeout is reached (typically logout)
 * @param timeoutMs - Timeout duration in ms (default: 30 minutes)
 * @param enabled - Whether the timer is active (disable for unauthenticated users)
 */
export function useInactivityTimeout(
  onTimeout: () => void,
  timeoutMs: number = INACTIVITY_TIMEOUT_MS,
  enabled: boolean = true,
): void {
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const onTimeoutRef = useRef(onTimeout)

  // Keep callback ref current without re-running effects
  onTimeoutRef.current = onTimeout

  const resetTimer = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current)
    }
    timerRef.current = setTimeout(() => {
      onTimeoutRef.current()
    }, timeoutMs)
  }, [timeoutMs])

  useEffect(() => {
    if (!enabled) {
      if (timerRef.current) {
        clearTimeout(timerRef.current)
        timerRef.current = null
      }
      return
    }

    // Start initial timer
    resetTimer()

    // Reset timer on user activity
    const handleActivity = () => resetTimer()

    for (const event of ACTIVITY_EVENTS) {
      document.addEventListener(event, handleActivity, { passive: true })
    }

    return () => {
      if (timerRef.current) {
        clearTimeout(timerRef.current)
      }
      for (const event of ACTIVITY_EVENTS) {
        document.removeEventListener(event, handleActivity)
      }
    }
  }, [enabled, resetTimer])
}
