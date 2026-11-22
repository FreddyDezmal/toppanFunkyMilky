import type { ReactNode } from 'react'
import { MilkshakeIllustration } from '../illustrations/MilkshakeIllustration'

interface AuthLayoutProps {
  title: string
  description: string
  children: ReactNode
  /** Line under the form, e.g. the link to the other auth page. */
  footer?: ReactNode
}

/**
 * Split screen for log in and registration: a brand panel beside the form on wider screens, the form alone on phones.
 * Only the layout lives here; the forms, validation and security behaviour stay in the pages.
 */
export function AuthLayout({ title, description, children, footer }: AuthLayoutProps) {
  return (
    <div className="mx-auto grid max-w-4xl overflow-hidden rounded-card border border-line bg-surface shadow-soft md:grid-cols-[0.85fr_1.15fr]">
      <div className="relative hidden flex-col justify-between bg-strawberry-tint p-8 md:flex">
        <div>
          <p className="font-display text-3xl leading-tight font-semibold text-chocolate">Your Milkshake. Your Rules.</p>
          <p className="mt-3 text-sm text-chocolate">Build it, pay online and collect it at the time you choose.</p>
        </div>
        <MilkshakeIllustration className="mx-auto mt-6 w-full max-w-60" animated={false} onTint />
      </div>
      <div className="p-6 sm:p-10">
        <h1 className="text-3xl font-semibold text-chocolate">{title}</h1>
        <p className="mt-2 text-warm-grey">{description}</p>
        {children}
        {footer && <p className="mt-8 text-center text-sm text-warm-grey">{footer}</p>}
      </div>
    </div>
  )
}
