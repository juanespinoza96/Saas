/// <reference types="vitest" />
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    css: false,
    // Aislar cada archivo de test en su propio proceso y liberar la memoria del heap
    // al terminar. Se prefieren "forks" sobre "threads" por una liberación de memoria
    // más determinista, lo que evita el OOM ("Worker exited unexpectedly").
    pool: 'forks',
    isolate: true,
    poolOptions: {
      forks: {
        // Acotar la concurrencia para limitar la memoria agregada por corrida.
        maxForks: 2,
        minForks: 1,
        // Se baja de 6144 a 4096 MB a propósito: un heap enorme solo RETRASA y OCULTA
        // la fuga de memoria monótona (el OOM también ocurre en single fork). Con un
        // límite razonable, si la fuga persiste el OOM se manifiesta temprano y es
        // diagnosticable; si el reset de mocks (setup.ts) la corrige, la suite cabe holgada.
        execArgv: ['--max-old-space-size=4096'],
      },
    },
  },
})
