import { useEffect, useRef, useState, type FormEvent } from 'react'
import { TextField } from '../../components/forms/TextField'
import { focusFirstError } from '../../components/forms/focusFirstError'
import { createCatalogItem, updateCatalogItem } from './api'
import { useDisplayFormat } from './displayFormat'
import { isConflict, splitServerErrors } from './errors'
import { CATALOG_LABELS, type CatalogItem, type CatalogKind } from './types'
import {
  hasFieldErrors,
  parseAmount,
  validateCatalogForm,
  type CatalogFormErrors,
  type CatalogFormValues,
} from './validation'


const FIELDS = ['name', 'displayOrder', 'initialPrice'] as const

interface CatalogItemFormProps {
  kind: CatalogKind
  /** The item to edit; omit to add a new one. */
  item?: CatalogItem
  onSaved: (item: CatalogItem) => void
  onCancel: () => void
  /** Called on 409: someone else changed the item since it was loaded. */
  onConflict: () => void
}

/** Add or edit a flavour, topping or consistency. A new item needs its starting price. */
export function CatalogItemForm({ kind, item, onSaved, onCancel, onConflict }: CatalogItemFormProps) {
  const { currencyCode } = useDisplayFormat()
  const singular = CATALOG_LABELS[kind].singular
  const [values, setValues] = useState<CatalogFormValues>({
    name: item?.name ?? '',
    displayOrder: String(item?.displayOrder ?? 0),
    initialPrice: '',
  })
  const [errors, setErrors] = useState<CatalogFormErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const savingRef = useRef(false) // blocks a second submit even before React re-renders
  // Server errors arrive while the inputs are disabled (saving). A disabled input cannot take focus,
  // so remember the errors and move focus once saving has finished and the inputs are enabled again.
  const focusAfterSaveRef = useRef<CatalogFormErrors | null>(null)

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

    const found = validateCatalogForm(values, item === undefined)
    setErrors(found)
    if (hasFieldErrors(found)) {
      focusFirstError(FIELDS, found)
      return
    }

    savingRef.current = true
    setSaving(true)
    try {
      const name = values.name.trim()
      const displayOrder = Number(values.displayOrder.trim())
      const saved =
        item === undefined
          ? await createCatalogItem(kind, { name, displayOrder, initialPrice: parseAmount(values.initialPrice) ?? 0 })
          : await updateCatalogItem(kind, item.id, { name, displayOrder, version: item.version })
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
      aria-labelledby="catalog-form-title"
      onSubmit={(event) => void handleSubmit(event)}
      className="space-y-4 rounded-xl border border-brand-100 bg-brand-50/50 p-5"
    >
      <h2 id="catalog-form-title" className="text-lg font-semibold">
        {item === undefined ? `Add a ${singular}` : `Edit ${item.name}`}
      </h2>

      {formError && (
        <div role="alert" className="rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
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
        id="displayOrder"
        label="Display order"
        inputMode="numeric"
        hint="Lower numbers appear first in the patron's list."
        value={values.displayOrder}
        error={errors.displayOrder}
        disabled={saving}
        onChange={(displayOrder) => setValues((current) => ({ ...current, displayOrder }))}
      />
      {item === undefined && (
        <TextField
          id="initialPrice"
          label="Price"
          inputMode="decimal"
          hint={`${currencyCode ? `Amount in ${currencyCode}. ` : ''}Takes effect immediately; later changes can be scheduled on the item's price page.`}
          value={values.initialPrice}
          error={errors.initialPrice}
          disabled={saving}
          onChange={(initialPrice) => setValues((current) => ({ ...current, initialPrice }))}
        />
      )}

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