import { Link } from 'react-router'
import { hasRole, useAuth } from '../features/auth/authContext'
import { ROLES } from '../features/auth/types'
import { SystemStatusCard } from '../features/system-status/SystemStatusCard'

export function HomePage() {
  const { status, user } = useAuth()

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Welcome</h1>
        <p className="mt-2 text-slate-600">
          Foundation build: this page confirms that the frontend can reach the API, and the API can reach the database.
        </p>
      </div>

      <section aria-labelledby="order-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h2 id="order-heading" className="text-xl font-semibold">
          Order milkshakes for pickup
        </h2>
        <p className="mt-1 text-slate-600">Choose your milkshakes, a restaurant and a pickup time.</p>
        {hasRole(user, ROLES.patron) && (
          <Link
            to="/order"
            className="mt-4 inline-block rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
          >
            Start an order
          </Link>
        )}
        {status === 'anonymous' && (
          <p className="mt-4 text-sm text-slate-600">
            <Link to="/login" className="font-medium text-brand-700 underline">
              Log in to order
            </Link>{' '}
            or{' '}
            <Link to="/register" className="font-medium text-brand-700 underline">
              create an account
            </Link>
            .
          </p>
        )}
      </section>

      <SystemStatusCard />
    </div>
  )
}