import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { routeTitle } from '../../app/routeTitle'
import { hasRole, useAuth } from '../../features/auth/authContext'
import { ROLES } from '../../features/auth/types'
import { Alert } from '../ui/Alert'
import { Icon } from '../ui/Icon'
import { Skeleton } from '../ui/Skeleton'

function navLinkClass({ isActive }: { isActive: boolean }) {
  return `flex min-h-11 items-center rounded-full px-4 text-sm font-semibold transition-colors md:min-h-10 ${
    isActive ? 'bg-strawberry-tint text-raspberry-dark' : 'text-chocolate hover:bg-strawberry-wash hover:text-raspberry-dark'
  }`
}

/**
 * Shared page frame: sticky header with the wordmark, main navigation and session controls, routed content, footer.
 * On narrow screens the navigation folds behind one "Menu" button (one nav element in both layouts, so the links and
 * their names are the same everywhere). The menu closes on navigation and with Escape.
 */
export function AppLayout() {
  const { status, user, error, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [signingOut, setSigningOut] = useState(false)
  // The menu belongs to the page it was opened on: navigating anywhere closes it without an effect.
  const [menuOpenOn, setMenuOpenOn] = useState<string | null>(null)
  const menuOpen = menuOpenOn === location.pathname

  // Every page has its own tab title (WCAG 2.4.2).
  useEffect(() => {
    document.title = routeTitle(location.pathname)
  }, [location.pathname])

  // After moving to another page (not on the first load), focus goes to the new page's heading, so keyboard and
  // screen-reader users start at the new content instead of the link they used (or the top of the document).
  const firstPath = useRef(location.pathname)
  useEffect(() => {
    if (firstPath.current === location.pathname) {
      return undefined
    }
    firstPath.current = ''
    const frame = requestAnimationFrame(() => {
      const main = document.getElementById('main')
      const heading = main?.querySelector<HTMLElement>('h1')
      if (main?.contains(document.activeElement) && document.activeElement !== main) {
        return // the page already placed focus itself (for example on an error)
      }
      if (heading) {
        if (!heading.hasAttribute('tabindex')) {
          heading.setAttribute('tabindex', '-1')
        }
        heading.focus()
      } else {
        main?.focus()
      }
    })
    return () => cancelAnimationFrame(frame)
  }, [location.pathname])

  async function handleLogout() {
    setSigningOut(true)
    try {
      await logout()
    } finally {
      setSigningOut(false)
      setMenuOpenOn(null)
      void navigate('/login', { replace: true })
    }
  }

  function handleMenuKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'Escape' && menuOpen) {
      setMenuOpenOn(null)
      document.getElementById('main-menu-button')?.focus()
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-cream text-chocolate">
      <a
        href="#main"
        className="sr-only z-50 rounded-full bg-raspberry px-4 py-2 font-semibold text-white focus:not-sr-only focus:fixed focus:top-3 focus:left-3"
      >
        Skip to main content
      </a>

      <header className="sticky top-0 z-40 border-b border-line bg-surface/95 backdrop-blur" onKeyDown={handleMenuKeyDown}>
        <div className="mx-auto flex max-w-(--container-page) flex-wrap items-center justify-between gap-x-4 px-4 py-3 sm:px-6">
          <Link to="/" className="flex min-h-11 items-center gap-2 rounded-full pr-2">
            <span className="flex size-9 items-center justify-center rounded-full bg-strawberry-tint text-raspberry">
              <Icon name="glass" className="size-5" />
            </span>
            <span className="font-display text-xl font-semibold tracking-tight text-chocolate">Milky Shaky</span>
          </Link>

          <button
            id="main-menu-button"
            type="button"
            aria-expanded={menuOpen}
            aria-controls="main-nav"
            onClick={() => setMenuOpenOn(menuOpen ? null : location.pathname)}
            className="flex min-h-11 items-center gap-2 rounded-full border border-line-strong px-4 text-sm font-semibold text-chocolate md:hidden"
          >
            <Icon name={menuOpen ? 'close' : 'menu'} />
            Menu
          </button>

          <nav
            id="main-nav"
            aria-label="Main"
            className={`${menuOpen ? 'flex' : 'hidden'} w-full flex-col gap-1 border-t border-line pt-3 pb-2 md:flex md:w-auto md:flex-row md:items-center md:border-0 md:p-0`}
          >
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
                <div className="mt-2 flex items-center justify-between gap-3 border-t border-line pt-3 md:mt-0 md:ml-2 md:border-0 md:border-l md:pt-0 md:pl-4">
                  <span className="text-sm text-warm-grey">Hi, {user.firstName}</span>
                  <button
                    type="button"
                    onClick={() => void handleLogout()}
                    disabled={signingOut}
                    className="flex min-h-10 items-center gap-1.5 rounded-full border border-chocolate/30 px-4 text-sm font-semibold text-chocolate hover:border-chocolate hover:bg-strawberry-wash disabled:opacity-50"
                  >
                    <Icon name="logout" className="size-4" />
                    {signingOut ? 'Logging out…' : 'Log out'}
                  </button>
                </div>
              </>
            )}
            {status === 'loading' && (
              // Holds the space the account links will take, so the header does not jump when the session loads.
              <span className="flex items-center gap-2 px-2 md:ml-2">
                <Skeleton className="h-4 w-20" />
                <Skeleton className="h-9 w-24 rounded-full" />
              </span>
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
        <div className="mx-auto mt-4 w-full max-w-(--container-page) px-4 sm:px-6">
          <Alert tone="warning">{error}</Alert>
        </div>
      )}

      <main id="main" tabIndex={-1} className="mx-auto w-full max-w-(--container-page) flex-1 px-4 py-8 focus:outline-none sm:px-6 sm:py-10">
        <Outlet />
      </main>

      <footer className="border-t border-line bg-surface/60">
        <div className="mx-auto flex max-w-(--container-page) flex-col gap-1 px-4 py-6 text-sm text-warm-grey sm:flex-row sm:items-center sm:justify-between sm:px-6">
          <p className="font-display font-semibold text-chocolate">Milky Shaky</p>
          <p>Custom milkshakes, made to order for pickup.</p>
        </div>
      </footer>
    </div>
  )
}
