/** Role names exactly as issued by the API. */
export const ROLES = { patron: 'Patron', manager: 'Manager' } as const

/** The signed-in user (GET /api/auth/me). */
export interface CurrentUser {
  id: string
  firstName: string
  email: string
  mobileNumber: string | null
  roles: string[]
}

export interface LoginInput {
  email: string
  password: string
}

export interface RegisterInput {
  firstName: string
  email: string
  mobileNumber: string
  password: string
  confirmPassword: string
}