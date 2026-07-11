import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// In dev, the Vite server proxies API calls so the browser only talks to
// one origin:
//   /api  → the ASP.NET Core render API
//   /lint → the Python lint service
export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.RENDER_API_URL ?? 'http://localhost:5133',
        changeOrigin: true,
      },
      '/lint': {
        target: process.env.LINT_API_URL ?? 'http://localhost:8000',
        changeOrigin: true,
      },
    },
  },
})
