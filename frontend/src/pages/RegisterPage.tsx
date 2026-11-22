import { useState, type FormEvent } from 'react'
import { Link, Navigate } from 'react-router'
import { TextField } from '../components/forms/TextField'
import { AuthLayout } from '../components/layout/AuthLayout'
import { Alert } from '../components/ui/Alert'
import { Button } from '../components/ui/Button'
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
    <AuthLayout
      title="Create your account"
      description="Order ahead and collect your milkshake without the queue."
      footer={
        <>
          Already have an account?{' '}
          <Link to="/login" className="font-semibold text-raspberry underline-offset-4 hover:underline">
            Log in
          </Link>
        </>
      }
    >
      {formError && (
        <Alert tone="danger" role="alert" className="mt-6">
          {formError}
        </Alert>
      )}

      <form noValidate onSubmit={(event) => void handleSubmit(event)} className="mt-6 space-y-5">
        <TextField id="firstName" label="First name" autoComplete="given-name" value={values.firstName} error={errors.firstName} onChange={update('firstName')} />
        <TextField id="email" label="Email address" type="email" autoComplete="email" value={values.email} error={errors.email} onChange={update('email')} />
        <TextField id="mobileNumber" label="Mobile number" type="tel" autoComplete="tel" hint={MOBILE_RULE} value={values.mobileNumber} error={errors.mobileNumber} onChange={update('mobileNumber')} />
        <TextField id="password" label="Password" type="password" autoComplete="new-password" hint={PASSWORD_RULE} value={values.password} error={errors.password} onChange={update('password')} />
        <TextField id="confirmPassword" label="Confirm password" type="password" autoComplete="new-password" value={values.confirmPassword} error={errors.confirmPassword} onChange={update('confirmPassword')} />
        <Button type="submit" size="lg" fullWidth disabled={submitting}>
          {submitting ? 'Creating account…' : 'Create account'}
        </Button>
      </form>
    </AuthLayout>
  )
}
