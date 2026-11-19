import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { SelectField } from '../../components/forms/SelectField'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { quantityChoices } from '../../features/ordering/draft'
import { useOrderDraft } from '../../features/ordering/orderDraftContext'
import { RECONCILE_MESSAGES } from '../../features/ordering/reconcileDraft'
import { DRINK_FIELDS } from '../../features/ordering/types'
import { useOrdering } from '../../features/ordering/useOrdering'

const FIELD_ID = 'quantity'

/** /order — how many milkshakes (URS: within the configured minimum and maximum). */
export function OrderQuantityPage() {
  const { options } = useOrdering()
  const { draft, dispatch } = useOrderDraft()
  const navigate = useNavigate()
  const choices = quantityChoices(options.drinkLimits)
  const { min, max } = options.drinkLimits
  const [quantity, setQuantity] = useState(choices.includes(draft.drinks.length) ? String(draft.drinks.length) : '')
  const [error, setError] = useState<string | undefined>()
  const [confirmRemoval, setConfirmRemoval] = useState(false)

  const chosen = Number(quantity)
  const current = draft.drinks.length

  function apply() {
    dispatch({ type: 'setQuantity', quantity: chosen })
    void navigate('/order/drinks')
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!choices.includes(chosen)) {
      setError(quantity === '' ? 'Choose how many milkshakes you would like.' : RECONCILE_MESSAGES.quantity(min, max))
      document.getElementById(FIELD_ID)?.focus()
      return
    }
    // Reducing the quantity drops milkshakes from the end: ask first if any of them has choices.
    const dropped = draft.drinks.slice(chosen)
    if (dropped.some((drink) => DRINK_FIELDS.some((field) => drink[field] !== null))) {
      setConfirmRemoval(true)
      return
    }
    apply()
  }

  const removalMessage =
    current - chosen === 1
      ? `Milkshake ${current} and its choices will be removed from your order.`
      : `Milkshakes ${chosen + 1} to ${current} and their choices will be removed from your order.`

  return (
    <section aria-labelledby="quantity-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 id="quantity-heading" className="text-2xl font-bold tracking-tight">
        How many milkshakes?
      </h1>
      <p className="mt-1 text-sm text-slate-600">Each milkshake gets its own flavour, consistency and topping.</p>

      <form noValidate onSubmit={handleSubmit} className="mt-6 max-w-xs space-y-4">
        <SelectField
          id={FIELD_ID}
          label="Number of milkshakes"
          value={quantity}
          placeholder="Choose a number"
          options={choices.map((choice) => ({ value: String(choice), label: String(choice) }))}
          hint={min === max ? `Each order has ${min} milkshake(s).` : `You can order ${min} to ${max} milkshakes.`}
          error={error}
          onChange={(value) => {
            setQuantity(value)
            setError(undefined)
          }}
        />
        <button type="submit" className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700">
          Continue
        </button>
      </form>

      <ConfirmDialog
        open={confirmRemoval}
        title="Remove milkshakes?"
        message={removalMessage}
        confirmLabel="Remove"
        cancelLabel="Keep them"
        onConfirm={() => {
          setConfirmRemoval(false)
          apply()
        }}
        onCancel={() => setConfirmRemoval(false)}
      />
    </section>
  )
}