import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation } from 'react-router'
import { TextField } from '../components/forms/TextField'
import { AuthLayout } from '../components/layout/AuthLayout'
import { Alert } from '../components/ui/Alert'
import { Button } from '../components/ui/Button'
import { focusFirstError } from '../components/forms/focusFirstError'
import { useAuth } from '../features/auth/authContext'
import type { LoginInput } from '../features/auth/types'
import { hasErrors, safeReturnPath, validateLogin, type FormErrors } from '../features/auth/validation'
import { describeError } from '../lib/api/describeError'

const FIELDS = ['email', 'password'] as const

export function LoginPage() {
  const { status, login } = useAuth()
  const location = useLocation()
  const [values, setValues] = useState<LoginInput>({ email: '', password: '' })
  const [errors, setErrors] = useState<FormErrors<LoginInput>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  // Signed in (also right after a successful login): continue to where the user was going.
  if (status === 'authenticated') {
    return <Navigate to={safeReturnPath(location.state)} replace />
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFormError(null)

    const found = validateLogin(values)
    setErrors(found)
    if (hasErrors(found)) {
      focusFirstError(FIELDS, found)
      return
    }

    setSubmitting(true)
    try {
      await login(values)
    } catch (error) {
      setFormError(describeError(error))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AuthLayout
      title="Log in to your account"
      description="Welcome back! Please enter your details."
      footer={
        <>
          {"Don't have an account? "}
          <Link to="/register" className="font-semibold text-raspberry underline-offset-4 hover:underline">
            Sign up
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
        <TextField
          id="email"
          label="Email address"
          type="email"
          autoComplete="email"
          value={values.email}
          error={errors.email}
          onChange={(email) => setValues((current) => ({ ...current, email }))}
        />
        <TextField
          id="password"
          label="Password"
          type="password"
          autoComplete="current-password"
          value={values.password}
          error={errors.password}
          onChange={(password) => setValues((current) => ({ ...current, password }))}
        />
        <Button type="submit" size="lg" fullWidth disabled={submitting}>
          {submitting ? 'Signing in…' : 'Log in'}
        </Button>
      </form>
    </AuthLayout>
  )
}
