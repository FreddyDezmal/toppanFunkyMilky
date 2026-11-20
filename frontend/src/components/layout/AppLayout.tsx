import { useState } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router'
import { hasRole, useAuth } from '../../features/auth/authContext'
import { ROLES } from '../../features/auth/types'

function navLinkClass({ isActive }: { isActive: boolean }) {
  return `rounded-md px-3 py-1.5 text-sm font-medium ${
    isActive ? 'bg-brand-100 text-brand-900' : 'text-slate-600 hover:text-brand-700'
  }`
}

/** Shared page frame: header with navigation and session controls, routed content, footer. */
export function AppLayout() {
  const { status, user, error, logout } = useAuth()
  const navigate = useNavigate()
  const [signingOut, setSigningOut] = useState(false)

  async function handleLogout() {
    setSigningOut(true)
    try {
      await logout()
    } finally {
      setSigningOut(false)
      void navigate('/login', { replace: true })
    }
  }

  return (
    <div className="min-h-screen bg-brand-50 text-slate-900">
      <header className="border-b border-brand-100 bg-white">
        <div className="mx-auto flex max-w-4xl flex-wrap items-center justify-between gap-4 px-4 py-4">
          <Link to="/" className="text-xl font-extrabold tracking-tight text-brand-600">
            Milky Shaky
          </Link>
          <nav aria-label="Main" className="flex flex-wrap items-center gap-1">
            <NavLink to="/" end className={navLinkClass}>
              Home
            </NavLink>
            {status === 'authenticated' && user && (
              <>

                {hasRole(user, ROLES.patron) && (
                  <>
                    <NavLink to="/order" className={navLinkClass}>
                      Order
                    </NavLink>
                    <NavLink to="/orders" end className={navLinkClass}>
                      My orders
                    </NavLink>
                  </>
                )}              

                <NavLink to="/account" className={navLinkClass}>
                  My account
                </NavLink>
                {hasRole(user, ROLES.manager) && (
                  <NavLink to="/manager" className={navLinkClass}>
                    Management
                  </NavLink>
                )}
                <span className="px-2 text-sm text-slate-500">Hi, {user.firstName}</span>
                <button
                  type="button"
                  onClick={() => void handleLogout()}
                  disabled={signingOut}
                  className="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                >
                  {signingOut ? 'Logging out…' : 'Log out'}
                </button>
              </>
            )}
            {status === 'anonymous' && (
              <>
                <NavLink to="/login" className={navLinkClass}>
                  Log in
                </NavLink>
                <NavLink to="/register" className={navLinkClass}>
                  Register
                </NavLink>
              </>
            )}
          </nav>
        </div>
      </header>

      {error && (
        <div className="mx-auto mt-4 max-w-4xl px-4">
          <p className="rounded-lg bg-amber-50 p-3 text-sm text-amber-800">{error}</p>
        </div>
      )}

      <main className="mx-auto max-w-4xl px-4 py-10">
        <Outlet />
      </main>
      <footer className="mx-auto max-w-4xl px-4 pb-8 text-xs text-slate-400">Development build</footer>
    </div>
  )
}