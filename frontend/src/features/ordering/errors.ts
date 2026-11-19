import { describeError } from '../../lib/api/describeError'
import { ApiError } from '../../lib/api/httpClient'

/** Shown when the API answers 503 without a usable message. */
export const ORDERING_UNAVAILABLE = 'Ordering is temporarily unavailable. Please try again later.'

/** Draft fields as the API names them in validation errors. */
export const DRAFT_FIELDS = {
  restaurantId: 'restaurantId',
  pickupLocal: 'pickupLocal',
  drinks: 'drinks',
  drink: (index: number) => `drinks[${index}]`,
  drinkChoice: (index: number, field: string) => `drinks[${index}].${field}`,
} as const

// restaurantId, pickupLocal, drinks, drinks[2], drinks[2].flavourId / consistencyId / toppingId
const KNOWN_FIELD = /^(restaurantId|pickupLocal|drinks(\[\d+\](\.(flavourId|consistencyId|toppingId))?)?)$/

export interface DraftErrors {
  /** First message per known draft field. */
  fields: Record<string, string>
  /** Everything else, for an alert above the form. */
  form: string | null
}

/** A message for the patron, giving 503 its own wording (configuration details stay in the server log). */
export function describeOrderingError(error: unknown): string {
  if (error instanceof ApiError && error.status === 503) {
    const { title, detail } = error.problem ?? {}
    return title && detail ? `${title} ${detail}` : ORDERING_UNAVAILABLE
  }
  return describeError(error)
}

/** Splits a validate error into field messages and a form-level message. */
export function draftErrorsFrom(error: unknown): DraftErrors {
  if (!(error instanceof ApiError) || error.status !== 400 || Object.keys(error.fieldErrors).length === 0) {
    return { fields: {}, form: describeOrderingError(error) }
  }

  const fields: Record<string, string> = {}
  const other: string[] = []
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const first = messages.at(0)
    if (!first) {
      continue
    }
    if (KNOWN_FIELD.test(field)) {
      fields[field] = first
    } else {
      other.push(first)
    }
  }
  return { fields, form: other.length > 0 ? other.join(' ') : null }
}

/** All messages about one milkshake (the entry itself and its three choices), in a stable order. */
export function drinkErrors(fields: Record<string, string>, index: number): string[] {
  const prefix = DRAFT_FIELDS.drink(index)
  return Object.keys(fields)
    .filter((key) => key === prefix || key.startsWith(`${prefix}.`))
    .sort()
    .map((key) => fields[key] ?? '')
    .filter((message) => message !== '')
}