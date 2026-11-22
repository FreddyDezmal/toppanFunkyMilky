import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { QuantityStepper } from '../../components/forms/QuantityStepper'
import { MilkshakeIllustration } from '../../components/illustrations/MilkshakeIllustration'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { Icon } from '../../components/ui/Icon'
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
  // Starts at the draft's quantity, or the smallest allowed order for a new draft.
  const [chosen, setChosen] = useState(choices.includes(draft.drinks.length) ? draft.drinks.length : min)
  const [error, setError] = useState<string | undefined>()
  const [confirmRemoval, setConfirmRemoval] = useState(false)

  const current = draft.drinks.length

  function apply() {
    dispatch({ type: 'setQuantity', quantity: chosen })
    void navigate('/order/drinks')
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!choices.includes(chosen)) {
      setError(RECONCILE_MESSAGES.quantity(min, max))
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
    <Card as="section" aria-labelledby="quantity-heading" className="mx-auto max-w-3xl">
      <div className="grid items-center gap-8 sm:grid-cols-[1fr_auto]">
        <div>
          <h1 id="quantity-heading" className="text-3xl font-semibold text-chocolate sm:text-4xl">
            How many milkshakes?
          </h1>
          <p className="mt-2 text-warm-grey">Each milkshake gets its own flavour, consistency and topping.</p>

          <form noValidate onSubmit={handleSubmit} className="mt-8 space-y-8">
            <QuantityStepper
              id={FIELD_ID}
              label="Number of milkshakes"
              value={chosen}
              min={min}
              max={max}
              unit={{ one: 'milkshake' }}
              hint={min === max ? `Each order has ${min} milkshake(s).` : `You can order ${min} to ${max} milkshakes.`}
              error={error}
              onChange={(value) => {
                setChosen(value)
                setError(undefined)
              }}
            />
            <Button type="submit" size="lg">
              Continue
              <Icon name="arrow-right" />
            </Button>
          </form>
        </div>
        <MilkshakeIllustration className="hidden w-44 sm:block" animated={false} />
      </div>

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
    </Card>
  )
}
