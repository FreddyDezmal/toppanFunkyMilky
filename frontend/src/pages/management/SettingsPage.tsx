import { useEffect, useState } from 'react'
import { listSettings } from '../../features/management/api'
import { useDisplayFormat } from '../../features/management/displayFormat'
import { CONFLICT_MESSAGE } from '../../features/management/errors'
import { SettingForm } from '../../features/management/SettingForm'
import { SETTING_KEYS, settingLabel, type Setting } from '../../features/management/types'
import { describeError } from '../../lib/api/describeError'
import { formatMoney, formatPercent } from '../../lib/format/money'

function displayValue(setting: Setting, locale?: string, currencyCode?: string): string {
  const number = Number(setting.value)
  switch (setting.key) {
    case SETTING_KEYS.vatRate:
    case SETTING_KEYS.refundFeeRate:
      return Number.isFinite(number) ? `${formatPercent(number, locale)} (${setting.value})` : setting.value
    case SETTING_KEYS.maxDiscountAmount:
      return currencyCode && Number.isFinite(number) ? formatMoney(number, currencyCode, locale) : setting.value
    case SETTING_KEYS.minPickupLeadTimeMinutes:
      return `${setting.value} minutes`
    default:
      return setting.value
  }
}

/** /manager/settings — management-configurable values (URS §3.4.6). */
export function SettingsPage() {
  const { locale, currencyCode, applySettings } = useDisplayFormat()
  const [settings, setSettings] = useState<Setting[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadCount, setReloadCount] = useState(0)
  const [editingKey, setEditingKey] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [problem, setProblem] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    listSettings(controller.signal)
      .then(setSettings)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(describeError(error))
        }
      })
    return () => controller.abort()
  }, [reloadCount])

  function reload() {
    setProblem(null)
    setLoadError(null)
    setReloadCount((count) => count + 1)
  }

  function handleSaved(updated: Setting[], key: string) {
    setSettings(updated)
    applySettings?.(updated)
    setEditingKey(null)
    setProblem(null)
    setNotice(`Saved “${settingLabel(key)}”. It applies to orders placed from now on.`)
  }

  function showConflict() {
    setEditingKey(null)
    setNotice(null)
    setProblem(CONFLICT_MESSAGE)
  }

  return (
    <section aria-labelledby="settings-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 id="settings-heading" className="text-2xl font-bold tracking-tight">
        Settings
      </h1>
      <p className="mt-1 text-sm text-slate-600">
        Changes apply to orders placed after you save. Existing orders keep the values they were placed with.
      </p>

      {notice && (
        <p role="status" className="mt-4 rounded-lg bg-emerald-50 p-3 text-sm text-emerald-800">
          {notice}
        </p>
      )}
      {problem && (
        <div role="alert" className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-lg bg-amber-50 p-3 text-sm text-amber-900">
          <span>{problem}</span>
          <button
            type="button"
            onClick={reload}
            className="rounded-md border border-amber-300 bg-white px-3 py-1.5 font-medium hover:bg-amber-100"
          >
            Reload
          </button>
        </div>
      )}

      {loadError ? (
        <p role="alert" className="mt-6 rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
          {loadError}
        </p>
      ) : settings === null ? (
        <p role="status" className="mt-6 text-sm text-slate-600">
          Loading settings…
        </p>
      ) : (
        <ul className="mt-6 divide-y divide-slate-100">
          {settings.map((setting) => {
            const label = settingLabel(setting.key)
            const isEditing = editingKey === setting.key
            return (
              <li key={setting.key} className="py-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <h2 className="font-medium text-slate-900">{label}</h2>
                    <p className="mt-0.5 text-sm text-slate-700">{displayValue(setting, locale, currencyCode)}</p>
                    {!isEditing && setting.hint && <p className="mt-1 text-xs text-slate-600">{setting.hint}</p>}
                  </div>
                  {setting.isEditable ? (
                    !isEditing && (
                      <button
                        type="button"
                        aria-label={`Edit ${label}`}
                        onClick={() => {
                          setNotice(null)
                          setEditingKey(setting.key)
                        }}
                        disabled={editingKey !== null}
                        className="rounded-md px-2 py-1 text-sm font-medium text-brand-700 hover:bg-brand-50 disabled:opacity-40"
                      >
                        Edit
                      </button>
                    )
                  ) : (
                    <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">Read-only</span>
                  )}
                </div>
                {isEditing && (
                  <SettingForm
                    setting={setting}
                    label={label}
                    onSaved={(updated) => handleSaved(updated, setting.key)}
                    onCancel={() => setEditingKey(null)}
                    onConflict={showConflict}
                  />
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}