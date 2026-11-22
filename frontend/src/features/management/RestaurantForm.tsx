import { useEffect, useRef, useState, type FormEvent } from 'react'
import { TextField } from '../../components/forms/TextField'
import { focusFirstError } from '../../components/forms/focusFirstError'
import { createRestaurant, updateRestaurant } from './api'
import { isConflict, splitServerErrors } from './errors'
import type { Restaurant } from './types'
import {
  hasFieldErrors,
  validateRestaurantForm,
  type RestaurantFormErrors,
  type RestaurantFormValues,
} from './validation'

const FIELDS = ['name', 'address'] as const

interface RestaurantFormProps {
  /** The restaurant to edit; omit to add a new one. */
  restaurant?: Restaurant
  onSaved: (restaurant: Restaurant) => void
  onCancel: () => void
  onConflict: () => void
}

/** Add or edit a pickup restaurant. */
export function RestaurantForm({ restaurant, onSaved, onCancel, onConflict }: RestaurantFormProps) {
  const [values, setValues] = useState<RestaurantFormValues>({
    name: restaurant?.name ?? '',
    address: restaurant?.address ?? '',
  })
  const [errors, setErrors] = useState<RestaurantFormErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const savingRef = useRef(false) // blocks a second submit even before React re-renders
  // Server errors arrive while the inputs are disabled; move focus once they are enabled again.
  const focusAfterSaveRef = useRef<RestaurantFormErrors | null>(null)

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

    const found = validateRestaurantForm(values)
    setErrors(found)
    if (hasFieldErrors(found)) {
      focusFirstError(FIELDS, found)
      return
    }

    savingRef.current = true
    setSaving(true)
    try {
      const name = values.name.trim()
      const address = values.address.trim() || null
      const saved =
        restaurant === undefined
          ? await createRestaurant({ name, address })
          : await updateRestaurant(restaurant.id, { name, address, version: restaurant.version })
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
      aria-labelledby="restaurant-form-title"
      onSubmit={(event) => void handleSubmit(event)}
      className="space-y-4 rounded-xl border border-line bg-strawberry-wash/60 p-5"
    >
      <h2 id="restaurant-form-title" className="text-lg font-semibold">
        {restaurant === undefined ? 'Add a restaurant' : `Edit ${restaurant.name}`}
      </h2>

      {formError && (
        <div role="alert" className="rounded-lg bg-danger-tint p-3 text-sm text-danger">
          {formError}
        </div>
      )}

      <TextField
        id="name"
        label="Name"
        value={values.name}
        error={errors.name}
        disabled={saving}
        onChange={(name) => setValues((current) => ({ ...current, name }))}
      />
      <TextField
        id="address"
        label="Address"
        required={false}
        hint="Optional. Shown to patrons when they choose where to collect."
        value={values.address}
        error={errors.address}
        disabled={saving}
        onChange={(address) => setValues((current) => ({ ...current, address }))}
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