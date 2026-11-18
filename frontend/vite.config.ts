/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    strictPort: true,
    // Same origin in development: the browser calls /api on Vite, which forwards to ASP.NET Core.
    // The SameSite=Strict session cookie then behaves exactly as it will behind one production domain.
    proxy: {
      '/api': {
        target: 'https://localhost:7298',
        secure: false, // Node does not trust the local ASP.NET Core development certificate
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Threads start much faster than child processes on Windows and avoid worker start-up timeouts.
    pool: 'threads',
    // Unit and component tests only; Playwright end-to-end tests live in /e2e.
    include: ['src/**/*.test.{ts,tsx}'],
  },
})