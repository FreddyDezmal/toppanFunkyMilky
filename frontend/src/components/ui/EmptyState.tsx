import type { ReactNode } from 'react'
import { Icon, type IconName } from './Icon'

interface EmptyStateProps {
  title: string
  children?: ReactNode
  icon?: IconName
  /** Optional call to action, e.g. a link styled with buttonClasses(). */
  action?: ReactNode
}

/** Friendly "nothing here yet" panel with an optional next step. */
export function EmptyState({ title, children, icon = 'glass', action }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center rounded-card border border-dashed border-line bg-surface/60 px-6 py-10 text-center">
      <span className="flex size-14 items-center justify-center rounded-full bg-strawberry-tint text-raspberry">
        <Icon name={icon} className="size-7" />
      </span>
      <p className="mt-4 font-display text-lg font-semibold text-chocolate">{title}</p>
      {children && <div className="mt-1 max-w-sm text-sm text-warm-grey">{children}</div>}
      {action && <div className="mt-5">{action}</div>}
    </div>
  )
}
