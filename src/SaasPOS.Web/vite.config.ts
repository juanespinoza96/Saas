import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

// Puerto del backend .NET local (ver GUIA_CLOUDFLARE_TUNNEL.md). Se puede
// sobrescribir con la variable de entorno API_PROXY_TARGET.
const API_TARGET = process.env.API_PROXY_TARGET || 'http://localhost:5291'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 3000,
    allowedHosts: true,
    // Proxy de /api al backend local. Permite que el frontend use rutas
    // relativas (mismo origen) y evita problemas de CORS durante desarrollo
    // y pruebas E2E. En producción/túnel se sigue usando VITE_API_BASE_URL.
    proxy: {
      '/api': {
        target: API_TARGET,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
