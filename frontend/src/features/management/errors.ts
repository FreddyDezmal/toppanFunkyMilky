import { describeError } from '../../lib/api/describeError'
import { ApiError } from '../../lib/api/httpClient'

/** Shown when the API answers 409: someone else saved first. */
export const CONFLICT_MESSAGE =
  'Someone else changed this since you loaded it. Reload to see the latest values, then make your change again.'

export function isConflict(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409
}

/**
 * Splits a 400 validation problem into messages for known form fields and a form-level message for the rest.
 * Any other error becomes a form-level message.
 */
export function splitServerErrors(
  error: unknown,
  fields: readonly string[],
): { fieldErrors: Record<string, string>; formError: string | null } {
  if (!(error instanceof ApiError) || error.status !== 400 || Object.keys(error.fieldErrors).length === 0) {
    return { fieldErrors: {}, formError: describeError(error) }
  }

  const fieldErrors: Record<string, string> = {}
  const other: string[] = []
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const first = messages.at(0)
    if (!first) {
      continue
    }
    if (fields.includes(field)) {
      fieldErrors[field] = first
    } else {
      other.push(first)
    }
  }
  return { fieldErrors, formError: other.length > 0 ? other.join(' ') : null }
}