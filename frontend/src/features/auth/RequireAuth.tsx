import { Navigate, Outlet, useLocation } from 'react-router'
import { hasRole, useAuth } from './authContext'

/**
 * Renders child routes only for signed-in users (optionally with a role).
 * This is for user experience; the API enforces the same rules on every request.
 */
export function RequireAuth({ role }: { role?: string }) {
  const { status, user } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return (
      <p role="status" className="text-sm text-slate-500">
        Checking your session…
      </p>
    )
  }

  if (status === 'anonymous' || !user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (role && !hasRole(user, role)) {
    return (
      <section className="rounded-2xl bg-white p-8 shadow-sm ring-1 ring-slate-200">
        <h1 className="text-2xl font-bold tracking-tight">Access denied</h1>
        <p className="mt-2 text-slate-600">{"You don't have access to this page."}</p>
      </section>
    )
  }

  return <Outlet />
}