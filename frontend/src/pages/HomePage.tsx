import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { MilkshakeIllustration } from '../components/illustrations/MilkshakeIllustration'
import { buttonClasses } from '../components/ui/buttonClasses'
import { Card } from '../components/ui/Card'
import { Icon, type IconName } from '../components/ui/Icon'
import { Skeleton } from '../components/ui/Skeleton'
import { hasRole, useAuth } from '../features/auth/authContext'
import { ROLES } from '../features/auth/types'
import { fetchOrderingOptions } from '../features/ordering/api'
import { CancellationRequestNotice } from '../features/ordering/CancellationRequestNotice'
import type { OrderingOption, OrderingOptions } from '../features/ordering/types'
import { formatMoney } from '../lib/format/money'

const STEPS: { icon: IconName; title: string; text: string }[] = [
  { icon: 'glass', title: 'Build it', text: 'Choose a flavour, a consistency and a topping for each milkshake.' },
  { icon: 'clock', title: 'Pick a time and pay', text: 'Choose the restaurant and pickup time, then pay online.' },
  { icon: 'store', title: 'Collect it', text: 'Collect your milkshakes from the restaurant at the time you chose.' },
]

/** Landing page. The menu comes from the live catalogue (patrons only: the API serves it to signed-in patrons). */
export function HomePage() {
  const { status, user } = useAuth()
  const isPatron = hasRole(user, ROLES.patron)
  const isManager = hasRole(user, ROLES.manager)

  return (
    <div className="space-y-16 sm:space-y-20">
      {isPatron && <CancellationRequestNotice />}

      <section aria-labelledby="hero-heading" className="grid items-center gap-8 md:grid-cols-[1.15fr_0.85fr]">
        <div>
          <p className="inline-flex items-center gap-2 rounded-full bg-strawberry-tint px-3 py-1 text-sm font-semibold text-raspberry-dark">
            <Icon name="glass" className="size-4" />
            Custom milkshakes for pickup
          </p>
          <h1 id="hero-heading" className="mt-5 text-5xl leading-[1.05] font-semibold text-chocolate sm:text-6xl">
            Your Milkshake. Your Rules.
          </h1>
          <p className="mt-5 max-w-xl text-lg text-warm-grey">
            Pick your flavour, choose your consistency, add your favourite topping, and collect your perfect shake.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            {isManager && !isPatron ? (
              <Link to="/manager" className={buttonClasses({ size: 'lg' })}>
                Go to management
                <Icon name="arrow-right" />
              </Link>
            ) : (
              <Link to="/order" className={buttonClasses({ size: 'lg' })}>
                Build Your Milkshake
                <Icon name="arrow-right" />
              </Link>
            )}
            {isPatron && (
              <Link to="/orders" className={buttonClasses({ variant: 'secondary', size: 'lg' })}>
                My orders
              </Link>
            )}
            {status === 'anonymous' && (
              <Link to="/register" className={buttonClasses({ variant: 'secondary', size: 'lg' })}>
                Create an account
              </Link>
            )}
          </div>
          {status === 'anonymous' && (
            <p className="mt-4 text-sm text-warm-grey">
              Already have an account?{' '}
              <Link to="/login" className="font-semibold text-raspberry underline-offset-4 hover:underline">
                Log in
              </Link>
            </p>
          )}
        </div>
        <MilkshakeIllustration className="mx-auto w-48 sm:w-64 md:w-full md:max-w-sm" />
      </section>

      <section aria-labelledby="how-heading">
        <h2 id="how-heading" className="text-3xl font-semibold text-chocolate">
          How it works
        </h2>
        <ol className="mt-6 grid gap-4 sm:grid-cols-3">
          {STEPS.map((step, index) => (
            <li key={step.title}>
              <Card padding="md" className="h-full">
                <div className="flex items-center gap-3">
                  <span className="flex size-11 items-center justify-center rounded-full bg-strawberry-tint text-raspberry">
                    <Icon name={step.icon} className="size-6" />
                  </span>
                  <span className="text-sm font-semibold text-warm-grey">Step {index + 1}</span>
                </div>
                <h3 className="mt-4 text-lg font-semibold text-chocolate">{step.title}</h3>
                <p className="mt-1 text-sm text-warm-grey">{step.text}</p>
              </Card>
            </li>
          ))}
        </ol>
      </section>

      {isPatron && <MenuSection />}
      {status === 'anonymous' && (
        <Card as="section" aria-labelledby="menu-heading" className="bg-strawberry-wash">
          <h2 id="menu-heading" className="text-3xl font-semibold text-chocolate">
            The menu
          </h2>
          <p className="mt-2 max-w-xl text-warm-grey">
            Log in or create an account to see every flavour, consistency and topping with its current price.
          </p>
          <Link to="/register" className={buttonClasses({ className: 'mt-6' })}>
            Create an account
          </Link>
        </Card>
      )}
    </div>
  )
}

type MenuState = { kind: 'loading' } | { kind: 'ready'; options: OrderingOptions } | { kind: 'unavailable' }

/** The live catalogue with current fees. Fees exclude VAT; the total is worked out by the server when ordering. */
function MenuSection() {
  const [state, setState] = useState<MenuState>({ kind: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    fetchOrderingOptions(controller.signal).then(
      (options) => setState({ kind: 'ready', options }),
      () => {
        if (!controller.signal.aborted) {
          setState({ kind: 'unavailable' })
        }
      },
    )
    return () => controller.abort()
  }, [])

  if (state.kind === 'unavailable') {
    // The order screens explain any problem in full; the landing page simply leaves the menu out.
    return null
  }

  return (
    <section aria-labelledby="menu-heading" aria-busy={state.kind === 'loading'}>
      <h2 id="menu-heading" className="text-3xl font-semibold text-chocolate">
        The menu
      </h2>
      <p className="mt-2 max-w-2xl text-warm-grey">
        Current prices for each choice, before VAT. Your total, with VAT and any discount, is worked out when you order.
      </p>
      {state.kind === 'loading' ? (
        <div className="mt-6 grid gap-4 md:grid-cols-3">
          {[0, 1, 2].map((key) => (
            <Card key={key} padding="md" className="space-y-3">
              <Skeleton className="h-5 w-28" />
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-5/6" />
              <Skeleton className="h-4 w-4/6" />
            </Card>
          ))}
        </div>
      ) : (
        <>
          <div className="mt-6 grid gap-4 md:grid-cols-3">
            <MenuList title="Flavours" items={state.options.flavours} locale={state.options.display.locale} />
            <MenuList title="Consistencies" items={state.options.consistencies} locale={state.options.display.locale} />
            <MenuList title="Toppings" items={state.options.toppings} locale={state.options.display.locale} />
          </div>
          {state.options.restaurants.length > 0 && (
            <p className="mt-6 flex flex-wrap items-center gap-2 text-sm text-warm-grey">
              <Icon name="store" className="size-5 text-raspberry" />
              <span>Collect from:</span>
              {state.options.restaurants.map((restaurant) => (
                <span key={restaurant.id} className="rounded-full bg-surface px-3 py-1 font-semibold text-chocolate ring-1 ring-line">
                  {restaurant.name}
                </span>
              ))}
            </p>
          )}
        </>
      )}
    </section>
  )
}

function MenuList({ title, items, locale }: { title: string; items: OrderingOption[]; locale: string }) {
  return (
    <Card padding="md">
      <h3 className="font-display text-xl font-semibold text-chocolate">{title}</h3>
      <ul className="mt-3 divide-y divide-line">
        {items.map((item) => (
          <li key={item.id} className="flex items-baseline justify-between gap-3 py-2 text-sm">
            <span className="font-semibold text-chocolate">{item.name}</span>
            <span className="shrink-0 text-warm-grey tabular-nums">
              {item.fee.amount === 0 ? 'Included' : formatMoney(item.fee.amount, item.fee.currencyCode, locale)}
            </span>
          </li>
        ))}
      </ul>
    </Card>
  )
}
