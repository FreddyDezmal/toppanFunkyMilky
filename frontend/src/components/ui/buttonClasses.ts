/** Shared button look, also for router <Link>s that start a journey (see buttonClasses). */
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
export type ButtonSize = 'sm' | 'md' | 'lg'

const base =
  'inline-flex items-center justify-center gap-2 rounded-full font-semibold transition-colors ' +
  'disabled:cursor-not-allowed disabled:opacity-50 aria-disabled:cursor-not-allowed aria-disabled:opacity-50'

const variants: Record<ButtonVariant, string> = {
  primary: 'bg-raspberry text-white shadow-soft hover:bg-raspberry-dark',
  secondary: 'border border-chocolate/30 bg-surface text-chocolate hover:border-chocolate hover:bg-strawberry-wash',
  ghost: 'text-raspberry hover:bg-strawberry-wash hover:text-raspberry-dark',
  danger: 'bg-danger text-white shadow-soft hover:bg-danger-dark',
}

// Every size keeps a target of at least 40 px high (WCAG 2.5.8 asks for 24 px); md and lg reach 44 px.
const sizes: Record<ButtonSize, string> = {
  sm: 'min-h-10 px-4 text-sm',
  md: 'min-h-11 px-5 text-sm',
  lg: 'min-h-12 px-7 text-base',
}

export interface ButtonStyleOptions {
  variant?: ButtonVariant
  size?: ButtonSize
  fullWidth?: boolean
  className?: string
}

/** Class names for anything that should look like a button, e.g. a router `<Link>` that starts a journey. */
export function buttonClasses({ variant = 'primary', size = 'md', fullWidth = false, className = '' }: ButtonStyleOptions = {}) {
  return [base, variants[variant], sizes[size], fullWidth ? 'w-full' : '', className].filter(Boolean).join(' ')
}
