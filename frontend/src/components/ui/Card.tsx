import type { ElementType, HTMLAttributes } from 'react'

interface CardProps extends HTMLAttributes<HTMLElement> {
  /** Element to render, e.g. "section" or "li" (default "div"). */
  as?: ElementType
  /** Inner spacing: "md" for dense manager panels, "lg" (default) for patron pages. */
  padding?: 'none' | 'md' | 'lg'
}

const paddings = { none: '', md: 'p-5', lg: 'p-6 sm:p-8' } as const

/** White rounded surface on the cream page. */
export function Card({ as: Component = 'div', padding = 'lg', className = '', ...rest }: CardProps) {
  return (
    <Component
      className={`rounded-card border border-line bg-surface shadow-soft ${paddings[padding]} ${className}`.trim()}
      {...rest}
    />
  )
}
