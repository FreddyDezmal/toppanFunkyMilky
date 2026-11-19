import { useEffect, useState } from 'react'
import { NavLink, Outlet } from 'react-router'
import { listSettings } from './api'
import { DisplayFormatContext, type DisplayFormat } from './displayFormat'
import { CATALOG_KINDS, CATALOG_LABELS, SETTING_KEYS } from './types'

function sectionLinkClass({ isActive }: { isActive: boolean }) {
  return `rounded-md px-3 py-1.5 text-sm font-medium ${
    isActive ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-brand-50 hover:text-brand-700'
  }`
}

/** Frame for all management pages: section navigation plus the display locale and currency from settings. */
export function ManagementLayout() {
  const [format, setFormat] = useState<DisplayFormat>({})

  useEffect(() => {
    const controller = new AbortController()
    listSettings(controller.signal)
      .then((settings) =>
        setFormat({
          locale: settings.find((s) => s.key === SETTING_KEYS.displayLocale)?.value,
          currencyCode: settings.find((s) => s.key === SETTING_KEYS.currencyCode)?.value,
        }),
      )
      .catch(() => {
        // Formatting falls back to the browser's locale; each page reports its own loading errors.
      })
    return () => controller.abort()
  }, [])

  return (
    <DisplayFormatContext value={format}>
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
      </nav>
      <Outlet />
    </DisplayFormatContext>
  )
}