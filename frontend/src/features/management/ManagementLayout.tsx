import { useEffect, useState, type KeyboardEvent } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router'
import { Icon, type IconName } from '../../components/ui/Icon'
import { listSettings } from './api'
import { DisplayFormatContext, type DisplayFormat } from './displayFormat'
import { CATALOG_KINDS, CATALOG_LABELS, SETTING_KEYS, type Setting } from './types'

type Format = Pick<DisplayFormat, 'locale' | 'currencyCode' | 'timeZone'>

interface Section {
  to: string
  label: string
  icon: IconName
  end?: boolean
}

/** The management sections, grouped as in the approved design (Menu, Operations, Insights, Setup). */
const GROUPS: { title: string; sections: Section[] }[] = [
  {
    title: 'Menu',
    sections: CATALOG_KINDS.map((kind) => ({ to: `/manager/catalog/${kind}`, label: CATALOG_LABELS[kind].title, icon: 'glass' })),
  },
  {
    title: 'Operations',
    sections: [
      { to: '/manager/orders', label: 'Orders', icon: 'receipt' },
      { to: '/manager/emails', label: 'Emails', icon: 'mail' },
    ],
  },
  { title: 'Insights', sections: [{ to: '/manager/reports', label: 'Reports', icon: 'chart' }] },
  {
    title: 'Setup',
    sections: [
      { to: '/manager/restaurants', label: 'Restaurants', icon: 'store' },
      { to: '/manager/settings', label: 'Settings', icon: 'settings' },
      { to: '/manager/discount-tiers', label: 'Discount tiers', icon: 'tag' },
    ],
  },
]

const ALL_SECTIONS: Section[] = [{ to: '/manager', label: 'Overview', icon: 'chart', end: true }, ...GROUPS.flatMap((group) => group.sections)]

function sectionLinkClass({ isActive }: { isActive: boolean }) {
  return `flex min-h-10 items-center gap-2.5 rounded-control px-3 text-sm font-semibold transition-colors ${
    isActive ? 'bg-raspberry text-white' : 'text-chocolate hover:bg-strawberry-wash'
  }`
}

function formatFrom(settings: Setting[]): Format {
  return {
    locale: settings.find((s) => s.key === SETTING_KEYS.displayLocale)?.value,
    currencyCode: settings.find((s) => s.key === SETTING_KEYS.currencyCode)?.value,
    timeZone: settings.find((s) => s.key === SETTING_KEYS.pickupTimeZone)?.value,
  }
}

function SectionLink({ section }: { section: Section }) {
  return (
    <NavLink to={section.to} end={section.end} className={sectionLinkClass}>
      <Icon name={section.icon} className="size-4 shrink-0" />
      {section.label}
    </NavLink>
  )
}

/**
 * Frame for all management pages: a grouped sidebar on wide screens and a "Sections" menu on phones (one nav element
 * either way), plus the display locale and currency from settings.
 */
export function ManagementLayout() {
  const [format, setFormat] = useState<Format>({})
  const { pathname } = useLocation()
  // The section menu belongs to the page it was opened on, so choosing a section closes it.
  const [menuOpenOn, setMenuOpenOn] = useState<string | null>(null)
  const menuOpen = menuOpenOn === pathname
  const current = ALL_SECTIONS.find((section) => (section.end ? pathname === section.to : pathname.startsWith(section.to)))

  useEffect(() => {
    const controller = new AbortController()
    listSettings(controller.signal)
      .then((settings) => setFormat(formatFrom(settings)))
      .catch(() => {
        // Formatting falls back to the browser's locale; each page reports its own loading errors.
      })
    return () => controller.abort()
  }, [])

  const value: DisplayFormat = { ...format, applySettings: (settings) => setFormat(formatFrom(settings)) }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape' && menuOpen) {
      setMenuOpenOn(null)
      document.getElementById('management-sections-button')?.focus()
    }
  }

  return (
    <DisplayFormatContext value={value}>
      <div className="grid gap-6 lg:grid-cols-[14rem_minmax(0,1fr)] lg:gap-8">
        <div onKeyDown={handleKeyDown} className="lg:sticky lg:top-24 lg:self-start">
          <button
            id="management-sections-button"
            type="button"
            aria-expanded={menuOpen}
            aria-controls="management-sections"
            onClick={() => setMenuOpenOn(menuOpen ? null : pathname)}
            className="flex min-h-11 w-full items-center justify-between gap-3 rounded-control border border-line-strong bg-surface px-4 text-left text-sm font-semibold text-chocolate lg:hidden"
          >
            <span>
              <span className="block text-xs font-semibold text-warm-grey">Management</span>
              {current?.label ?? 'Sections'}
            </span>
            <span className="flex items-center gap-1.5">
              Sections
              <Icon name={menuOpen ? 'close' : 'chevron-down'} className="size-4" />
            </span>
          </button>

          <nav
            id="management-sections"
            aria-label="Management sections"
            className={`${menuOpen ? 'block' : 'hidden'} mt-2 rounded-card border border-line bg-surface p-3 shadow-soft lg:mt-0 lg:block`}
          >
            <SectionLink section={ALL_SECTIONS[0]!} />
            {GROUPS.map((group) => (
              <div key={group.title} className="mt-4">
                <p className="px-3 pb-1 text-xs font-semibold tracking-wide text-warm-grey uppercase">{group.title}</p>
                <div className="space-y-0.5">
                  {group.sections.map((section) => (
                    <SectionLink key={section.to} section={section} />
                  ))}
                </div>
              </div>
            ))}
          </nav>
        </div>

        <div className="min-w-0">
          <Outlet />
        </div>
      </div>
    </DisplayFormatContext>
  )
}
