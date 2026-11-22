import { Navigate, Outlet, useLocation } from 'react-router'
import { Card } from '../../components/ui/Card'
import { Skeleton } from '../../components/ui/Skeleton'
import { hasRole, useAuth } from './authContext'

/**
 * Renders child routes only for signed-in users (optionally with a role).
 * This is for user experience; the API enforces the same rules on every request.
 */
export function RequireAuth({ role }: { role?: string }) {
  const { status, user } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    // Page-shaped placeholder instead of a blank screen; the status text is for screen readers.
    return (
      <div className="mx-auto max-w-4xl">
        <p role="status" className="sr-only">
          Checking your session…
        </p>
        <Skeleton className="h-9 w-64 max-w-full" />
        <Skeleton className="mt-3 h-4 w-96 max-w-full" />
        <Card className="mt-8 space-y-4">
          <Skeleton className="h-5 w-40" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-5/6" />
          <Skeleton className="h-11 w-36 rounded-full" />
        </Card>
      </div>
    )
  }

  if (status === 'anonymous' || !user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (role && !hasRole(user, role)) {
    return (
      <Card as="section" className="mx-auto max-w-3xl">
        <h1 className="text-3xl font-semibold">Access denied</h1>
        <p className="mt-2 text-warm-grey">{"You don't have access to this page."}</p>
      </Card>
    )
  }

  return <Outlet />
}