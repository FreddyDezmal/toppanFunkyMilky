import type { ReactNode } from 'react'
import { Icon, type IconName } from './Icon'

export type AlertTone = 'info' | 'success' | 'warning' | 'danger'

const tones: Record<AlertTone, { box: string; icon: IconName }> = {
  info: { box: 'border-line bg-strawberry-wash text-chocolate', icon: 'info' },
  success: { box: 'border-pistachio bg-pistachio-tint text-pistachio-deep', icon: 'check-circle' },
  warning: { box: 'border-warning/30 bg-warning-tint text-warning', icon: 'alert' },
  danger: { box: 'border-danger/30 bg-danger-tint text-danger', icon: 'alert' },
}

interface AlertProps {
  tone?: AlertTone
  title?: string
  children: ReactNode
  /**
   * "alert" announces immediately (errors after a submit), "status" announces politely; leave it unset for text that
   * is part of the page from the start.
   */
  role?: 'alert' | 'status'
  className?: string
}

/** Message box with an icon; the words carry the meaning, the colour and icon only reinforce it. */
export function Alert({ tone = 'info', title, children, role, className = '' }: AlertProps) {
  const style = tones[tone]
  return (
    <div role={role} className={`flex gap-3 rounded-control border p-4 text-sm ${style.box} ${className}`.trim()}>
      <Icon name={style.icon} className="mt-0.5 size-5 shrink-0" />
      <div className="min-w-0">
        {title && <p className="font-semibold">{title}</p>}
        <div className={title ? 'mt-1' : undefined}>{children}</div>
      </div>
    </div>
  )
}
