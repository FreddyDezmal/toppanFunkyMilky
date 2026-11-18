import { createContext, useContext } from 'react'
import type { CurrentUser, LoginInput, RegisterInput } from './types'

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

export interface AuthContextValue {
  status: AuthStatus
  user: CurrentUser | null
  /** Set when the session check could not reach the API. */
  error: string | null
  login: (input: LoginInput) => Promise<CurrentUser>
  register: (input: RegisterInput) => Promise<CurrentUser>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)
  if (!value) {
    throw new Error('useAuth must be used inside <AuthProvider>.')
  }
  return value
}

export function hasRole(user: CurrentUser | null, role: string): boolean {
  return user?.roles.includes(role) ?? false
}