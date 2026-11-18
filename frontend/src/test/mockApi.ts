import { vi } from 'vitest'

export type ApiHandler = (init: RequestInit | undefined) => Response | Promise<Response>
export type ApiRoutes = Record<string, ApiHandler>

export function jsonResponse(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: () => Promise.resolve(body) } as Response
}

/**
 * Replaces fetch with a tiny router keyed by "METHOD /path". The CSRF endpoint is always available;
 * any other unexpected call fails the request, which surfaces as an error in the UI under test.
 */
export function mockApi(routes: ApiRoutes) {
  const all: ApiRoutes = {
    'GET /api/auth/csrf': () => jsonResponse({ token: 'test-token', headerName: 'X-XSRF-TOKEN' }),
    ...routes,
  }

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const key = `${init?.method ?? 'GET'} ${new URL(url, 'http://localhost').pathname}`
    const handler = all[key]
    if (!handler) {
      throw new Error(`Unexpected API call: ${key}`)
    }
    return handler(init)
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}