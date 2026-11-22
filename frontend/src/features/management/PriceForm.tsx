import { useEffect, useRef, useState, type FormEvent } from 'react'
import { TextField } from '../../components/forms/TextField'
import { focusFirstError } from '../../components/forms/focusFirstError'
import { schedulePrice } from './api'
import { useDisplayFormat } from './displayFormat'
import { isConflict, splitServerErrors } from './errors'
import type { CatalogKind, PriceHistory } from './types'
import {
  hasFieldErrors,
  parseAmount,
  validatePriceForm,
  type PriceFormErrors,
  type PriceFormValues,
} from './validation'

const FIELDS = ['amount', 'effectiveFrom'] as const

interface PriceFormProps {
  kind: CatalogKind
  history: PriceHistory
  onScheduled: (updated: PriceHistory, immediate: boolean) => void
  onConflict: () => void
}

/** Changes an item's price now, or schedules it for a future moment. Uses the item version from the history. */
export function PriceForm({ kind, history, onScheduled, onConflict }: PriceFormProps) {
  const { currencyCode } = useDisplayFormat()
  const currency = history.item.currencyCode ?? currencyCode
  const [values, setValues] = useState<PriceFormValues>({ amount: '', when: 'now', effectiveFrom: '' })
  const [errors, setErrors] = useState<PriceFormErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const savingRef = useRef(false) // blocks a second submit even before React re-renders
  // Server errors arrive while the inputs are disabled; move focus once they are enabled again.
  const focusAfterSaveRef = useRef<PriceFormErrors | null>(null)

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

    const found = validatePriceForm(values, new Date())
    setErrors(found)
    if (hasFieldErrors(found)) {
      focusFirstError(FIELDS, found)
      return
    }

    const immediate = values.when === 'now'
    savingRef.current = true
    setSaving(true)
    try {
      const updated = await schedulePrice(kind, history.item.id, {
        amount: parseAmount(values.amount) ?? 0,
        effectiveFrom: immediate ? null : new Date(values.effectiveFrom).toISOString(),
        version: history.item.version,
      })
      onScheduled(updated, immediate)
    } catch (error) {
      if (isConflict(error)) {
        onConflict()
        return
      }
      const server = splitServerErrors(error, FIELDS)
      // With "Immediately" the start field is hidden, so its message is shown at the top of the form.
      const { effectiveFrom: startError, ...otherErrors } = server.fieldErrors
      const fieldErrors = immediate ? otherErrors : server.fieldErrors
      setErrors(fieldErrors)
      setFormError([server.formError, immediate ? startError : undefined].filter(Boolean).join(' ') || null)
      focusAfterSaveRef.current = fieldErrors
    } finally {
      savingRef.current = false
      setSaving(false)
    }
  }

  return (
    <form
      noValidate
      aria-labelledby="price-form-title"
      onSubmit={(event) => void handleSubmit(event)}
      className="space-y-4 rounded-xl border border-line bg-strawberry-wash/60 p-5"
    >
      <h2 id="price-form-title" className="text-lg font-semibold">
        Change the price
      </h2>

      {formError && (
        <div role="alert" className="rounded-lg bg-danger-tint p-3 text-sm text-danger">
          {formError}
        </div>
      )}

      <TextField
        id="amount"
        label="New price"
        inputMode="decimal"
        hint={currency ? `Amount in ${currency}.` : undefined}
        value={values.amount}
        error={errors.amount}
        disabled={saving}
        onChange={(amount) => setValues((current) => ({ ...current, amount }))}
      />

      <fieldset disabled={saving}>
        <legend className="text-sm font-medium text-chocolate">When should it start?</legend>
        <div className="mt-2 space-y-2">
          <label className="flex items-center gap-2 text-sm">
            <input
              type="radio"
              name="when"
              value="now"
              checked={values.when === 'now'}
              onChange={() => setValues((current) => ({ ...current, when: 'now' }))}
            />
            Immediately
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="radio"
              name="when"
              value="later"
              checked={values.when === 'later'}
              onChange={() => setValues((current) => ({ ...current, when: 'later' }))}
            />
            At a later date and time
          </label>
        </div>
      </fieldset>

      {values.when === 'later' && (
        <TextField
          id="effectiveFrom"
          label="Starts at"
          type="datetime-local"

          hint="Your local time. It must be in the future."
          value={values.effectiveFrom}
          error={errors.effectiveFrom}
          disabled={saving}
          onChange={(effectiveFrom) => setValues((current) => ({ ...current, effectiveFrom }))}
        />
      )}

      <button
        type="submit"
        disabled={saving}
        className="rounded-lg bg-raspberry px-4 py-2 text-sm font-medium text-white hover:bg-raspberry-dark disabled:cursor-not-allowed disabled:opacity-50"
      >
        {saving ? 'Saving…' : values.when === 'now' ? 'Change price now' : 'Schedule price'}
      </button>
    </form>
  )
}