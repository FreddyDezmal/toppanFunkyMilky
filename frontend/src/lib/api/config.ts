/**
 * Returns the API base URL without a trailing slash.
 * Throws a clear message (shown in the UI) when the environment variable is missing.
 */
export function getApiBaseUrl(): string {
  const value = import.meta.env.VITE_API_BASE_URL

  if (!value) {
    throw new Error(
      'VITE_API_BASE_URL is not set. Copy frontend/.env.example to frontend/.env.development.local and restart "npm run dev".',
    )
  }

  return value.replace(/\/+$/, '')
}