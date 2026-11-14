import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // Fixed port: the API's CORS policy allows exactly this origin.
    port: 5173,
    strictPort: true,
  },
})