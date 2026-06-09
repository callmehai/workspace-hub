import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // Dev: proxy /api → backend ASP.NET (tránh CORS). Prod: set VITE_API_URL.
    proxy: {
      '/api': {
        target: 'http://localhost:5118',
        changeOrigin: true,
      },
    },
  },
})
