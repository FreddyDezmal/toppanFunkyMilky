/**
 * Small set of original line icons drawn for Milky Shaky (24 × 24 grid, 1.75 stroke, currentColor), so no icon package
 * is needed. Icons are decorative by default (aria-hidden); pass `label` when an icon is the only content of a control.
 */
const paths = {
  menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  close: <path d="M6 6l12 12M18 6L6 18" />,
  check: <path d="M5 12.5l4.5 4.5L19 7.5" />,
  'check-circle': (
    <>
      <circle cx="12" cy="12" r="8.5" />
      <path d="M8.5 12.2l2.4 2.4 4.6-4.8" />
    </>
  ),
  info: (
    <>
      <circle cx="12" cy="12" r="8.5" />
      <path d="M12 11v5M12 8h.01" />
    </>
  ),
  alert: (
    <>
      <path d="M12 4l9 15.5H3L12 4z" />
      <path d="M12 10v4M12 17h.01" />
    </>
  ),
  plus: <path d="M12 5v14M5 12h14" />,
  minus: <path d="M5 12h14" />,
  'chevron-right': <path d="M9.5 6l6 6-6 6" />,
  'chevron-left': <path d="M14.5 6l-6 6 6 6" />,
  'chevron-down': <path d="M6 9.5l6 6 6-6" />,
  'arrow-right': <path d="M5 12h14M13 6l6 6-6 6" />,
  glass: (
    <>
      <path d="M6.5 8.5h11l-1.6 11.2a1.5 1.5 0 01-1.5 1.3H9.6a1.5 1.5 0 01-1.5-1.3L6.5 8.5z" />
      <path d="M6 8.5c0-2.2 2.7-4 6-4s6 1.8 6 4" />
      <path d="M13.5 4.6L15 2" />
    </>
  ),
  user: (
    <>
      <circle cx="12" cy="8.5" r="3.5" />
      <path d="M5 20c.8-3.4 3.6-5.5 7-5.5s6.2 2.1 7 5.5" />
    </>
  ),
  logout: <path d="M10 5H6.5A1.5 1.5 0 005 6.5v11A1.5 1.5 0 006.5 19H10M14 8l4 4-4 4M18 12H9.5" />,
  receipt: <path d="M7 3.5h10v17l-2.5-1.6L12 20.5l-2.5-1.6L7 20.5v-17zM9.5 8h5M9.5 11.5h5M9.5 15h3" />,
  store: <path d="M4 9.5L5.5 4.5h13L20 9.5M4 9.5c0 1.4 1.2 2.5 2.7 2.5s2.6-1.1 2.6-2.5c0 1.4 1.2 2.5 2.7 2.5s2.7-1.1 2.7-2.5c0 1.4 1.1 2.5 2.6 2.5S20 10.9 20 9.5M4 9.5h16M5.5 12v7.5h13V12M10 19.5v-4.5h4v4.5" />,
  clock: (
    <>
      <circle cx="12" cy="12" r="8.5" />
      <path d="M12 7.5V12l3 2" />
    </>
  ),
  chart: <path d="M4 20h16M7 16.5v-5M12 16.5V7M17 16.5v-8" />,
  settings: (
    <>
      <circle cx="12" cy="12" r="3" />
      <path d="M12 3.5v2.2M12 18.3v2.2M3.5 12h2.2M18.3 12h2.2M6 6l1.6 1.6M16.4 16.4L18 18M6 18l1.6-1.6M16.4 7.6L18 6" />
    </>
  ),
  mail: (
    <>
      <rect x="3.5" y="5.5" width="17" height="13" rx="1.5" />
      <path d="M4 7l8 6 8-6" />
    </>
  ),
  tag: (
    <>
      <path d="M3.5 12.3V4.5a1 1 0 011-1h7.8L20.5 11.7a1 1 0 010 1.4l-7.4 7.4a1 1 0 01-1.4 0L3.5 12.3z" />
      <circle cx="8" cy="8" r="1.3" />
    </>
  ),
} as const

export type IconName = keyof typeof paths

interface IconProps {
  name: IconName
  className?: string
  /** Accessible name; only when the icon is the sole content of its control. */
  label?: string
}

export function Icon({ name, className = 'size-5', label }: IconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden={label ? undefined : 'true'}
      role={label ? 'img' : undefined}
      aria-label={label}
      focusable="false"
    >
      {paths[name]}
    </svg>
  )
}
