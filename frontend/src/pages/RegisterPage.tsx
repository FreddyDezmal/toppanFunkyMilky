import { useState, type FormEvent } from 'react'
import { Link, Navigate } from 'react-router'
import { TextField } from '../components/forms/TextField'
import { focusFirstError } from '../components/forms/focusFirstError'
import { useAuth } from '../features/auth/authContext'
import type { RegisterInput } from '../features/auth/types'
import {
  hasErrors,
  mapServerErrors,
  MOBILE_RULE,
  PASSWORD_RULE,
  validateRegistration,
  type FormErrors,
} from '../features/auth/validation'
import { describeError } from '../lib/api/describeError'
import { ApiError } from '../lib/api/httpClient'

const FIELDS = ['firstName', 'email', 'mobileNumber', 'password', 'confirmPassword'] as const
const EMPTY: RegisterInput = { firstName: '', email: '', mobileNumber: '', password: '', confirmPassword: '' }

export function RegisterPage() {
  const { status, register } = useAuth()
  const [values, setValues] = useState<RegisterInput>(EMPTY)
  const [errors, setErrors] = useState<FormErrors<RegisterInput>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  if (status === 'authenticated') {
    return <Navigate to="/account" replace />
  }

  function update(field: keyof RegisterInput) {
    return (value: string) => setValues((current) => ({ ...current, [field]: value }))
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFormError(null)

    const found = validateRegistration(values)
    setErrors(found)
    if (hasErrors(found)) {
      focusFirstError(FIELDS, found)
      return
    }

    setSubmitting(true)
    try {
      await register(values)
    } catch (error) {
      if (error instanceof ApiError && error.status === 400 && hasErrors(error.fieldErrors)) {
        const mapped = mapServerErrors<RegisterInput>(error.fieldErrors, FIELDS)
        setErrors(mapped.fieldErrors)
        setFormError(mapped.formError)
        focusFirstError(FIELDS, mapped.fieldErrors)
      } else {
        setFormError(describeError(error))
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="mx-auto max-w-md rounded-2xl bg-white p-8 shadow-sm ring-1 ring-slate-200">
      <h1 className="text-2xl font-bold tracking-tight">Create your account</h1>
      <p className="mt-1 text-sm text-slate-600">Order ahead and collect your milkshake without the queue.</p>

      {formError && (
        <div role="alert" className="mt-6 rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
          {formError}
        </div>
      )}

      <form noValidate onSubmit={(event) => void handleSubmit(event)} className="mt-6 space-y-4">
        <TextField id="firstName" label="First name" autoComplete="given-name" value={values.firstName} error={errors.firstName} onChange={update('firstName')} />
        <TextField id="email" label="Email address" type="email" autoComplete="email" value={values.email} error={errors.email} onChange={update('email')} />
        <TextField id="mobileNumber" label="Mobile number" type="tel" autoComplete="tel" hint={MOBILE_RULE} value={values.mobileNumber} error={errors.mobileNumber} onChange={update('mobileNumber')} />
        <TextField id="password" label="Password" type="password" autoComplete="new-password" hint={PASSWORD_RULE} value={values.password} error={errors.password} onChange={update('password')} />
        <TextField id="confirmPassword" label="Confirm password" type="password" autoComplete="new-password" value={values.confirmPassword} error={errors.confirmPassword} onChange={update('confirmPassword')} />
        <button
          type="submit"
          disabled={submitting}
          className="w-full rounded-lg bg-brand-600 px-4 py-2 font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {submitting ? 'Creating account…' : 'Create account'}
        </button>
      </form>

      <p className="mt-6 text-center text-sm text-slate-600">
        Already have an account?{' '}
        <Link to="/login" className="font-medium text-brand-700 hover:underline">
          Log in
        </Link>
      </p>
    </div>
  )
}