import { ApiError } from './httpClient'

/** A message that is safe and useful to show the user. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) {
      return 'Too many attempts. Please wait a minute and try again.'
    }
    return error.message
  }
  return 'Something went wrong. Please try again.'
}