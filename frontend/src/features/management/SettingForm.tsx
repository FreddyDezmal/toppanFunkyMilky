import { useEffect, useRef, useState, type FormEvent } from 'react'
import { TextField } from '../../components/forms/TextField'
import { focusFirstError } from '../../components/forms/focusFirstError'
import { updateSetting } from './api'
import { isConflict, splitServerErrors } from './errors'
import type { Setting } from './types'

const FIELDS = ['value'] as const
type SettingErrors = { value?: string }

interface SettingFormProps {
  setting: Setting
  label: string
  /** Receives all settings, because saving one can change another's version. */
  onSaved: (updated: Setting[]) => void
  onCancel: () => void
  onConflict: () => void
}

/**
 * Edits one setting. The limits are checked by the server, the single source of truth, and its message appears
 * on the field.
 */
export function SettingForm({ setting, label, onSaved, onCancel, onConflict }: SettingFormProps) {
  const [value, setValue] = useState(setting.value)
  const [errors, setErrors] = useState<SettingErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const savingRef = useRef(false) // blocks a second submit even before React re-renders
  // Server errors arrive while the input is disabled; move focus once it is enabled again.
  const focusAfterSaveRef = useRef<SettingErrors | null>(null)

  // Put the cursor in the field when the form opens.
  useEffect(() => {
    document.getElementById('value')?.focus()
  }, [])

  useEffect(() => {
    if (!saving && focusAfterSaveRef.current) {
      focusFirstError(FIELDS, focusAfterSaveRef.current)
      focusAfterSaveRef.current = null
    }
  }, [saving])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (savingRef.current) {
      return
    }
    setFormError(null)

    if (!value.trim()) {
      const found = { value: 'A value is required.' }
      setErrors(found)
      focusFirstError(FIELDS, found)
      return
    }
    setErrors({})

    savingRef.current = true
    setSaving(true)
    try {
      onSaved(await updateSetting(setting.key, value.trim(), setting.version))
    } catch (error) {
      if (isConflict(error)) {
        onConflict()
        return
      }
      const server = splitServerErrors(error, FIELDS)
      setErrors(server.fieldErrors)
      setFormError(server.formError)
      focusAfterSaveRef.current = server.fieldErrors
    } finally {
      savingRef.current = false
      setSaving(false)
    }
  }

  const inputMode =
    setting.valueType === 'WholeNumber' ? 'numeric' : setting.valueType === 'DecimalNumber' ? 'decimal' : 'text'

  return (
    <form
      noValidate
      aria-label={`Edit ${label}`}
      onSubmit={(event) => void handleSubmit(event)}
      className="mt-3 space-y-3 rounded-xl border border-brand-100 bg-brand-50/50 p-4"
    >
      {formError && (
        <div role="alert" className="rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
          {formError}
        </div>
      )}
      <TextField
        id="value"
        label={label}
        inputMode={inputMode}
        hint={setting.hint}
        value={value}
        error={errors.value}
        disabled={saving}
        onChange={setValue}
      />
      <div className="flex gap-2">
        <button
          type="submit"
          disabled={saving}
          className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
        <button
          type="button"
          onClick={onCancel}
          disabled={saving}
          className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
        >
          Cancel
        </button>
      </div>
    </form>
  )
}