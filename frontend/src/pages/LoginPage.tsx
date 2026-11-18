import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation } from 'react-router'
import { TextField } from '../components/forms/TextField'
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
    <div className="mx-auto max-w-md rounded-2xl bg-white p-8 shadow-sm ring-1 ring-slate-200">
      <h1 className="text-2xl font-bold tracking-tight">Log in to your account</h1>
      <p className="mt-1 text-sm text-slate-600">Welcome back! Please enter your details.</p>

      {formError && (
        <div role="alert" className="mt-6 rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
          {formError}
        </div>
      )}

      <form noValidate onSubmit={(event) => void handleSubmit(event)} className="mt-6 space-y-4">
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
        <button
          type="submit"
          disabled={submitting}
          className="w-full rounded-lg bg-brand-600 px-4 py-2 font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {submitting ? 'Signing in…' : 'Log in'}
        </button>
      </form>

      <p className="mt-6 text-center text-sm text-slate-600">
        {"Don't have an account? "}
        <Link to="/register" className="font-medium text-brand-700 hover:underline">
          Sign up
        </Link>
      </p>
    </div>
  )
}