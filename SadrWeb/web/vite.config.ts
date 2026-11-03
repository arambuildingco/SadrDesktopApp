import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The SPA talks to the REST API under /api; in development Vite proxies it to the
// ASP.NET Core process so cookies, CORS and relative URLs behave like production.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: '0.0.0.0',
    proxy: {
      '/api': { target: 'http://127.0.0.1:5199', changeOrigin: true },
      '/openapi': { target: 'http://127.0.0.1:5199', changeOrigin: true }
    }
  },
  build: { outDir: 'dist', emptyOutDir: true }
})
