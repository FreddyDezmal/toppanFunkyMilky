import { getJson } from '../../lib/api/httpClient'

/** Manager-only probe; the API answers 403 for anyone else. */
export function fetchManagerPing(signal?: AbortSignal): Promise<{ message: string }> {
  return getJson<{ message: string }>('/api/manager/ping', { signal })
}