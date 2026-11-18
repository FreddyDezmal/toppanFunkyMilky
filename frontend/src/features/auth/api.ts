import { ApiError, clearCsrfToken, getJson, postJson } from '../../lib/api/httpClient'
import type { CurrentUser, LoginInput, RegisterInput } from './types'

/** The signed-in user, or null when there is no session. */
export async function fetchCurrentUser(signal?: AbortSignal): Promise<CurrentUser | null> {
  try {
    return await getJson<CurrentUser>('/api/auth/me', { signal })
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null
    }
    throw error
  }
}

export async function login(input: LoginInput): Promise<CurrentUser> {
  try {
    return await postJson<CurrentUser>('/api/auth/login', input)
  } finally {
    clearCsrfToken()
  }
}

/** Registers a patron (the API signs them in) and returns the full profile. */
export async function register(input: RegisterInput): Promise<CurrentUser> {
  try {
    await postJson('/api/auth/register', input)
  } finally {
    clearCsrfToken()
  }

  const user = await fetchCurrentUser()
  if (!user) {
    throw new ApiError('Your account was created, but signing in failed. Please log in.')
  }
  return user
}

export async function logout(): Promise<void> {
  try {
    await postJson<void>('/api/auth/logout')
  } finally {
    clearCsrfToken()
  }
}