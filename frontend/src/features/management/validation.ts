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


/** Price change form. effectiveFrom is a datetime-local value ("2026-12-01T09:00") in the manager's time zone. */
export interface PriceFormValues {
  amount: string
  when: 'now' | 'later'
  effectiveFrom: string
}

export type PriceFormErrors = Partial<Record<'amount' | 'effectiveFrom', string>>

export function validatePriceForm(values: PriceFormValues, now: Date): PriceFormErrors {
  const errors: PriceFormErrors = {}

  if (!values.amount.trim()) {
    errors.amount = 'Price is required.'
  } else if (parseAmount(values.amount) === null) {
    errors.amount = 'Enter an amount such as 25.50, with at most two decimal places.'
  }

  if (values.when === 'later') {
    const start = new Date(values.effectiveFrom)
    if (!values.effectiveFrom) {
      errors.effectiveFrom = 'Choose the date and time the price starts.'
    } else if (Number.isNaN(start.getTime())) {
      errors.effectiveFrom = 'Enter a valid date and time.'
    } else if (start <= now) {
      errors.effectiveFrom = 'Choose a date and time in the future. Prices that have taken effect cannot be changed.'
    }
  }

  return errors
}

/** A Date as a datetime-local value in the user's time zone, e.g. for the input's min attribute. */
export function toLocalInputValue(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

export interface RestaurantFormValues {
  name: string
  address: string
}

export type RestaurantFormErrors = Partial<Record<keyof RestaurantFormValues, string>>

const ADDRESS_MAX_LENGTH = 250

export function validateRestaurantForm(values: RestaurantFormValues): RestaurantFormErrors {
  const errors: RestaurantFormErrors = {}

  const name = values.name.trim()
  if (!name) {
    errors.name = 'Name is required.'
  } else if (name.length > NAME_MAX_LENGTH) {
    errors.name = `Name cannot exceed ${NAME_MAX_LENGTH} characters.`
  }

  if (values.address.trim().length > ADDRESS_MAX_LENGTH) {
    errors.address = `Address cannot exceed ${ADDRESS_MAX_LENGTH} characters.`
  }

  return errors
}