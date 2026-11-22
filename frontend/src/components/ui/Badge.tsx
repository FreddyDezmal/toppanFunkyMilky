import type { ReactNode } from 'react'

export type BadgeTone = 'neutral' | 'success' | 'warning' | 'danger' | 'accent'

// Every tone keeps its text at 5.4:1 or better on its own background; meaning is always carried by the words too.
const tones: Record<BadgeTone, string> = {
  neutral: 'bg-cream text-chocolate ring-line',
  success: 'bg-pistachio-tint text-pistachio-deep ring-pistachio',
  warning: 'bg-warning-tint text-warning ring-warning/30',
  danger: 'bg-danger-tint text-danger ring-danger/30',
  accent: 'bg-strawberry-tint text-raspberry-dark ring-strawberry',
}

/** Small pill label, e.g. an order or catalogue status. */
export function Badge({ tone = 'neutral', children }: { tone?: BadgeTone; children: ReactNode }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold ring-1 ring-inset ${tones[tone]}`}
    >
      {children}
    </span>
  )
}
