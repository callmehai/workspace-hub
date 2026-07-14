import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // Dev: proxy /api → backend ASP.NET (tránh CORS). Prod: set VITE_API_URL.
    // Target HTTPS (profile "https" của Api: https://localhost:7010). secure:false để
    // chấp nhận dev cert tự ký. Chạy BE bằng: dotnet run --launch-profile https
    proxy: {
      // Hub SignalR nằm dưới /api/hubs/* nên chỉ cần 1 block /api (ws:true cho WebSocket upgrade).
      '/api': {
        target: 'https://localhost:7010',
        changeOrigin: true,
        secure: false,
        ws: true,
      },
    },
  },
})
