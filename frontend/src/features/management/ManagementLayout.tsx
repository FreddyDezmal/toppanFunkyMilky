import { useEffect, useState } from 'react'
import { NavLink, Outlet } from 'react-router'
import { listSettings } from './api'
import { DisplayFormatContext, type DisplayFormat } from './displayFormat'
import { CATALOG_KINDS, CATALOG_LABELS, SETTING_KEYS, type Setting } from './types'

type Format = Pick<DisplayFormat, 'locale' | 'currencyCode'>

function sectionLinkClass({ isActive }: { isActive: boolean }) {
  return `rounded-md px-3 py-1.5 text-sm font-medium ${
    isActive ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-brand-50 hover:text-brand-700'
  }`
}

function formatFrom(settings: Setting[]): Format {
  return {
    locale: settings.find((s) => s.key === SETTING_KEYS.displayLocale)?.value,
    currencyCode: settings.find((s) => s.key === SETTING_KEYS.currencyCode)?.value,
  }
}

/** Frame for all management pages: section navigation plus the display locale and currency from settings. */
export function ManagementLayout() {
  const [format, setFormat] = useState<Format>({})

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

  return (
    <DisplayFormatContext value={value}>
      <nav
        aria-label="Management sections"
        className="mb-6 flex flex-wrap gap-1 rounded-xl bg-white p-2 shadow-sm ring-1 ring-slate-200"
      >
        <NavLink to="/manager" end className={sectionLinkClass}>
          Overview
        </NavLink>
        {CATALOG_KINDS.map((kind) => (
          <NavLink key={kind} to={`/manager/catalog/${kind}`} className={sectionLinkClass}>
            {CATALOG_LABELS[kind].title}
          </NavLink>
        ))}
        <NavLink to="/manager/restaurants" className={sectionLinkClass}>
          Restaurants
        </NavLink>
        <NavLink to="/manager/settings" className={sectionLinkClass}>
          Settings
        </NavLink>
        <NavLink to="/manager/discount-tiers" className={sectionLinkClass}>
          Discount tiers
        </NavLink>
      </nav>
      <Outlet />
    </DisplayFormatContext>
  )
}