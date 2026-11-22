import { useEffect, useRef, useState, type FormEvent } from 'react'
import { TextField } from '../../components/forms/TextField'
import { focusFirstError } from '../../components/forms/focusFirstError'
import { updateDiscountTier } from './api'
import { isConflict, splitServerErrors } from './errors'
import type { DiscountTier } from './types'
import { hasFieldErrors, parseRate, validateTierForm, type TierFormErrors, type TierFormValues } from './validation'

const FIELDS = ['minPreviousOrders', 'minDrinksPerOrder', 'discountRate'] as const

interface DiscountTierFormProps {
  tier: DiscountTier
  onSaved: (tier: DiscountTier) => void
  onCancel: () => void
  onConflict: () => void
}

/** Edits a tier's thresholds and rate. Editing never activates the tier. */
export function DiscountTierForm({ tier, onSaved, onCancel, onConflict }: DiscountTierFormProps) {
  const [values, setValues] = useState<TierFormValues>({
    minPreviousOrders: String(tier.minPreviousOrders),
    minDrinksPerOrder: String(tier.minDrinksPerOrder),
    discountRate: String(tier.discountRate),
  })
  const [errors, setErrors] = useState<TierFormErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const savingRef = useRef(false) // blocks a second submit even before React re-renders
  // Server errors arrive while the inputs are disabled; move focus once they are enabled again.
  const focusAfterSaveRef = useRef<TierFormErrors | null>(null)

  // Put the cursor in the first field when the form opens.
  useEffect(() => {
    document.getElementById('minPreviousOrders')?.focus()
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

    const found = validateTierForm(values)
    setErrors(found)
    if (hasFieldErrors(found)) {
      focusFirstError(FIELDS, found)
      return
    }

    savingRef.current = true
    setSaving(true)
    try {
      const saved = await updateDiscountTier(tier.tierNumber, {
        minPreviousOrders: Number(values.minPreviousOrders.trim()),
        minDrinksPerOrder: Number(values.minDrinksPerOrder.trim()),
        discountRate: parseRate(values.discountRate) ?? 0,
        version: tier.version,
      })
      onSaved(saved)
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

  return (
    <form
      noValidate
      aria-labelledby="tier-form-title"
      onSubmit={(event) => void handleSubmit(event)}
      className="space-y-4 rounded-xl border border-line bg-strawberry-wash/60 p-5"
    >
      <h2 id="tier-form-title" className="text-lg font-semibold">
        Edit tier {tier.tierNumber}
      </h2>

      {formError && (
        <div role="alert" className="rounded-lg bg-danger-tint p-3 text-sm text-danger">
          {formError}
        </div>
      )}

      <TextField
        id="minPreviousOrders"
        label="Paid orders needed"
        inputMode="numeric"
        hint="How many earlier paid orders a patron needs before this tier applies."
        value={values.minPreviousOrders}
        error={errors.minPreviousOrders}
        disabled={saving}
        onChange={(minPreviousOrders) => setValues((current) => ({ ...current, minPreviousOrders }))}
      />
      <TextField
        id="minDrinksPerOrder"
        label="Drinks per order"
        inputMode="numeric"
        hint="How many drinks an order needs to count towards this tier."
        value={values.minDrinksPerOrder}
        error={errors.minDrinksPerOrder}
        disabled={saving}
        onChange={(minDrinksPerOrder) => setValues((current) => ({ ...current, minDrinksPerOrder }))}
      />
      <TextField
        id="discountRate"
        label="Discount rate"
        inputMode="decimal"
        hint="Fraction from 0 to 1, e.g. 0.05 for 5%. At most four decimal places."
        value={values.discountRate}
        error={errors.discountRate}
        disabled={saving}
        onChange={(discountRate) => setValues((current) => ({ ...current, discountRate }))}
      />

      <div className="flex gap-2">
        <button
          type="submit"
          disabled={saving}
          className="rounded-lg bg-raspberry px-4 py-2 text-sm font-medium text-white hover:bg-raspberry-dark disabled:cursor-not-allowed disabled:opacity-50"
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
        <button
          type="button"
          onClick={onCancel}
          disabled={saving}
          className="rounded-lg border border-line-strong px-4 py-2 text-sm font-medium text-chocolate hover:bg-strawberry-wash disabled:opacity-50"
        >
          Cancel
        </button>
      </div>
    </form>
  )
}