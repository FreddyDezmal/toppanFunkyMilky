import { getApiBaseUrl } from './config'

/** Validation messages keyed by field name (camelCase, as sent). */
export type FieldErrors = Record<string, string[]>

/** RFC 9457 problem details as returned by the API. */
export interface ProblemDetails {
  status?: number
  title?: string
  detail?: string
  errors?: FieldErrors
  /** RFC 9457 extension members, e.g. currentTotals on a price-changed conflict. */
  [extension: string]: unknown
}

/** An API call that failed: unreachable server or an unexpected HTTP status. */
export class ApiError extends Error {
  readonly status: number | undefined
  readonly problem: ProblemDetails | undefined

  constructor(message: string, status?: number, problem?: ProblemDetails) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  /** Field-level validation messages, when the API returned a 400 validation problem. */
  get fieldErrors(): FieldErrors {
    return this.problem?.errors ?? {}
  }
}

interface RequestOptions {
  signal?: AbortSignal
  /** Extra request headers, e.g. Idempotency-Key. */
  headers?: Record<string, string>
  /** Non-2xx statuses whose JSON body is still meaningful (e.g. 503 from the health check). */
  acceptStatuses?: readonly number[]
}

const CSRF_HEADER = 'X-XSRF-TOKEN'
const CSRF_REJECTED_TITLE = 'Invalid or missing anti-forgery token.'
let csrfToken: string | undefined

/** Forgets the cached anti-forgery token. Call after sign-in, registration and sign-out: tokens are tied to the user. */
export function clearCsrfToken(): void {
  csrfToken = undefined
}

/** GETs a JSON resource from the API. */
export function getJson<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return request<T>('GET', path, undefined, options)
}

/** POSTs JSON (or no body) with the anti-forgery token. Resolves to the JSON body, or undefined for 204. */
export function postJson<T>(path: string, body?: unknown, options: RequestOptions = {}): Promise<T> {
  return request<T>('POST', path, body, options)
}


/** PUTs JSON with the anti-forgery token. Resolves to the JSON body. */
export function putJson<T>(path: string, body: unknown, options: RequestOptions = {}): Promise<T> {
  return request<T>('PUT', path, body, options)
}

async function request<T>(
  method: 'GET' | 'POST' | 'PUT',
  path: string,
  body: unknown,
  options: RequestOptions,
  isRetry = false,
): Promise<T> {
  const url = `${getApiBaseUrl()}${path}`
  const headers: Record<string, string> = { ...options.headers, Accept: 'application/json' }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  if (method !== 'GET') {
    headers[CSRF_HEADER] = await getCsrfToken(options.signal)
  }

  const response = await send(url, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'include',
    signal: options.signal,
  })

  if (response.ok || (options.acceptStatuses ?? []).includes(response.status)) {
    return (response.status === 204 ? undefined : await response.json()) as T
  }

  const problem = await readProblem(response)

  // A token belongs to one user; after a session change elsewhere, get a fresh one and retry once.
  if (method !== 'GET' && !isRetry && response.status === 400 && problem?.title === CSRF_REJECTED_TITLE) {
    clearCsrfToken()
    return request<T>(method, path, body, options, true)
  }

  throw new ApiError(
    problem?.detail ?? problem?.title ?? `The API returned HTTP ${response.status}.`,
    response.status,
    problem,
  )
}

async function getCsrfToken(signal?: AbortSignal): Promise<string> {
  if (csrfToken) {
    return csrfToken
  }

  const response = await send(`${getApiBaseUrl()}/api/auth/csrf`, {
    headers: { Accept: 'application/json' },
    credentials: 'include',
    signal,
  })
  if (!response.ok) {
    throw new ApiError(`Could not obtain an anti-forgery token (HTTP ${response.status}).`, response.status)
  }

  const body = (await response.json()) as { token: string }
  csrfToken = body.token
  return csrfToken
}

async function send(url: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(url, init)
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error
    }
    throw new ApiError(`Could not reach the API at ${url}. Is it running?`)
  }
}

async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}