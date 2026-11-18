import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import * as authApi from './api'
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext'
import type { CurrentUser, LoginInput, RegisterInput } from './types'

/**
 * Holds who is signed in. The session itself lives in an HTTP-only cookie that JavaScript cannot read;
 * this provider only asks the API (GET /api/auth/me) and keeps the answer in memory, never in storage.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    authApi
      .fetchCurrentUser(controller.signal)
      .then((current) => {
        if (controller.signal.aborted) {
          return
        }
        setUser(current)
        setStatus(current ? 'authenticated' : 'anonymous')
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        setUser(null)
        setStatus('anonymous')
        setError(reason instanceof Error ? reason.message : 'Could not check your session.')
      })

    return () => controller.abort()
  }, [])

  const signedIn = useCallback((current: CurrentUser) => {
    setUser(current)
    setStatus('authenticated')
    setError(null)
    return current
  }, [])

  const login = useCallback(async (input: LoginInput) => signedIn(await authApi.login(input)), [signedIn])

  const register = useCallback(async (input: RegisterInput) => signedIn(await authApi.register(input)), [signedIn])

  const logout = useCallback(async () => {
    try {
      await authApi.logout()
    } finally {
      // Clear local state even if the request failed, so the UI never shows a stale signed-in user.
      setUser(null)
      setStatus('anonymous')
    }
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, error, login, register, logout }),
    [status, user, error, login, register, logout],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}