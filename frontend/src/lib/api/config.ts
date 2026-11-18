/**
 * Base URL of the API without a trailing slash. Empty (the default) means "same origin":
 * Vite forwards /api in development, and production serves app and API from one domain.
 */
export function getApiBaseUrl(): string {
  return (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '')
}

/** Where API calls go, for display. */
export function describeApiTarget(): string {
  return getApiBaseUrl() || `${window.location.origin} (same origin)`
}