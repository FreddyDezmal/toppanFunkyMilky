/** Form values as typed; converted to numbers only after validation. */
export interface CatalogFormValues {
  name: string
  displayOrder: string
  initialPrice: string
}

export type CatalogFormErrors = Partial<Record<keyof CatalogFormValues, string>>

const NAME_MAX_LENGTH = 100
const MAX_INT = 2_147_483_647
const WHOLE_NUMBER = /^\d+$/
const AMOUNT = /^\d+(\.\d{1,2})?$/

/** Parses "25.50" or "25,50" (comma decimal separator) into a number; null if it is not a valid amount. */
export function parseAmount(value: string): number | null {
  const normalised = value.trim().replace(',', '.')
  return AMOUNT.test(normalised) ? Number(normalised) : null
}

/** Same rules as the API, checked first so the manager gets instant feedback. */
export function validateCatalogForm(values: CatalogFormValues, isNew: boolean): CatalogFormErrors {
  const errors: CatalogFormErrors = {}

  const name = values.name.trim()
  if (!name) {
    errors.name = 'Name is required.'
  } else if (name.length > NAME_MAX_LENGTH) {
    errors.name = `Name cannot exceed ${NAME_MAX_LENGTH} characters.`
  }

  const order = values.displayOrder.trim()
  if (!WHOLE_NUMBER.test(order) || Number(order) > MAX_INT) {
    errors.displayOrder = 'Enter a whole number of 0 or more.'
  }

  if (isNew) {
    if (!values.initialPrice.trim()) {
      errors.initialPrice = 'Price is required.'
    } else if (parseAmount(values.initialPrice) === null) {
      errors.initialPrice = 'Enter an amount such as 25.50, with at most two decimal places.'
    }
  }

  return errors
}

export function hasFieldErrors(errors: object): boolean {
  return Object.values(errors).some(Boolean)
}