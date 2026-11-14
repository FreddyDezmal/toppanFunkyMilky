import { getApiBaseUrl } from './config'

/** An API call that failed: unreachable server or an unexpected HTTP status. */
export class ApiError extends Error {
  readonly status: number | undefined

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

interface GetJsonOptions {
  signal?: AbortSignal
  /** Non-2xx statuses whose JSON body is still meaningful (e.g. 503 from the health check). */
  acceptStatuses?: readonly number[]
}

/** GETs a JSON resource from the API. */
export async function getJson<T>(path: string, options: GetJsonOptions = {}): Promise<T> {
  const url = `${getApiBaseUrl()}${path}`

  let response: Response
  try {
    response = await fetch(url, {
      headers: { Accept: 'application/json' },
      signal: options.signal,
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error
    }
    throw new ApiError(`Could not reach the API at ${url}. Is it running, and does its CORS policy allow this site?`)
  }

  const accepted = response.ok || (options.acceptStatuses ?? []).includes(response.status)
  if (!accepted) {
    throw new ApiError(`The API returned HTTP ${response.status}.`, response.status)
  }

  return (await response.json()) as T
}