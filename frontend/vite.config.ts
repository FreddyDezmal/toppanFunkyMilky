/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { readFileSync } from 'node:fs'

// HTTPS only for the local E2E stack (M13): e2e-local/run.mjs passes the exported ASP.NET development certificate.
// WebKit refuses Secure cookies on plain http://localhost; `npm run dev` is unchanged.
const e2eHttps =
  process.env.E2E_VITE_CERT && process.env.E2E_VITE_KEY
    ? { cert: readFileSync(process.env.E2E_VITE_CERT), key: readFileSync(process.env.E2E_VITE_KEY) }
    : undefined

// Same origin in development: the browser calls /api on Vite, which forwards to ASP.NET Core.
// The SameSite=Strict session cookie then behaves exactly as it will behind one production domain.
const apiProxy = {
  '/api': {
    target: 'https://localhost:7298',
    secure: false, // Node does not trust the local ASP.NET Core development certificate
  },
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    strictPort: true,
    https: e2eHttps,
    proxy: apiProxy,
  },
  // `vite preview` serves the production build for the local E2E stack (M13) on the same address as dev.
  preview: {
    port: 5173,
    strictPort: true,
    https: e2eHttps,
    proxy: apiProxy,
    // The M13 security and load tests run in Docker and reach this machine as host.docker.internal, which Docker
    // Desktop forwards to 127.0.0.1. "localhost" would listen on ::1 only on Windows, so the E2E stack binds IPv4.
    host: e2eHttps ? '127.0.0.1' : undefined,
    allowedHosts: e2eHttps ? ['host.docker.internal'] : undefined,
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