import type { LoginInput, RegisterInput } from './types'

/** Field name → the message to show next to that field. */
export type FormErrors<T> = Partial<Record<keyof T & string, string>>

export const PASSWORD_RULE = 'At least 8 characters, including an upper-case letter, a lower-case letter and a number.'
export const PASSWORD_TOO_WEAK = 'This password is too weak.'
export const MOBILE_RULE = 'International format, e.g. +27 82 123 4567.'

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const E164_PATTERN = /^\+[1-9]\d{7,14}$/

export function hasErrors(errors: object): boolean {
  return Object.keys(errors).length > 0
}

export function validateLogin(input: LoginInput): FormErrors<LoginInput> {
  const errors: FormErrors<LoginInput> = {}
  if (!input.email.trim()) {
    errors.email = 'Email address is required.'
  }
  if (!input.password) {
    errors.password = 'Password is required.'
  }
  return errors
}

export function validateRegistration(input: RegisterInput): FormErrors<RegisterInput> {
  const errors: FormErrors<RegisterInput> = {}
  const firstName = input.firstName.trim()
  const email = input.email.trim()
  const mobile = input.mobileNumber.replace(/[\s\-()]/g, '')
  const password = input.password

  if (!firstName) {
    errors.firstName = 'First name is required.'
  } else if (firstName.length > 100) {
    errors.firstName = 'First name cannot exceed 100 characters.'
  }

  if (!email) {
    errors.email = 'Email address is required.'
  } else if (!EMAIL_PATTERN.test(email)) {
    errors.email = 'Enter a valid email address.'
  }

  if (!mobile) {
    errors.mobileNumber = 'Mobile number is required.'
  } else if (!E164_PATTERN.test(mobile)) {
    errors.mobileNumber = `Enter your mobile number in international format, e.g. +27 82 123 4567.`
  }

  if (!password) {
    errors.password = 'Password is required.'
  } else if (password.length < 8 || !/[a-z]/.test(password) || !/[A-Z]/.test(password) || !/\d/.test(password)) {
    errors.password = PASSWORD_TOO_WEAK
  }

  if (!input.confirmPassword) {
    errors.confirmPassword = 'Please confirm your password.'
  } else if (input.confirmPassword !== password) {
    errors.confirmPassword = 'Passwords do not match.'
  }

  return errors
}

/** Places the API's validation messages next to known fields; anything else becomes a form-level message. */
export function mapServerErrors<T>(
  serverErrors: Record<string, string[]>,
  fields: readonly (keyof T & string)[],
): { fieldErrors: FormErrors<T>; formError: string | null } {
  const fieldErrors: FormErrors<T> = {}
  const other: string[] = []

  for (const [key, messages] of Object.entries(serverErrors)) {
    const message = messages[0]
    if (!message) {
      continue
    }
    if ((fields as readonly string[]).includes(key)) {
      fieldErrors[key as keyof T & string] = message
    } else {
      other.push(message)
    }
  }

  return { fieldErrors, formError: other.length > 0 ? other.join(' ') : null }
}

/** Accepts only in-app paths as a return target, so a crafted link cannot redirect elsewhere. */
export function safeReturnPath(state: unknown, fallback = '/account'): string {
  const from = (state as { from?: unknown } | null)?.from
  return typeof from === 'string' && from.startsWith('/') && !from.startsWith('//') && !from.includes('\\')
    ? from
    : fallback
}