import type { ReactNode } from 'react'

interface PageHeaderProps {
  title: string
  /** Short line under the title. */
  description?: ReactNode
  /** Small label above the title, e.g. "Step 2 of 4". */
  eyebrow?: string
  /** Buttons or links shown beside the title on wide screens and under it on narrow ones. */
  actions?: ReactNode
}

/** The page's one h1 with optional supporting text and actions. */
export function PageHeader({ title, description, eyebrow, actions }: PageHeaderProps) {
  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        {eyebrow && <p className="text-sm font-semibold text-raspberry">{eyebrow}</p>}
        <h1 className="text-3xl font-semibold text-chocolate sm:text-4xl">{title}</h1>
        {description && <div className="mt-2 max-w-2xl text-base text-warm-grey">{description}</div>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
    </div>
  )
}
