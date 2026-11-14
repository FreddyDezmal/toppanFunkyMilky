import { getJson } from '../../lib/api/httpClient'
import type { HealthResponse } from './types'

/** Calls GET /api/health. A 503 still carries a useful body (which check failed), so it is accepted. */
export function fetchHealth(signal?: AbortSignal): Promise<HealthResponse> {
  return getJson<HealthResponse>('/api/health', { signal, acceptStatuses: [503] })
}