import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { isDrinkComplete } from '../../features/ordering/draft'
import { DrinkCard } from '../../features/ordering/DrinkCard'
import { EmptyDraft } from '../../features/ordering/EmptyDraft'
import { DRAFT_FIELDS } from '../../features/ordering/errors'
import { FEES_NOTE } from '../../features/ordering/format'
import { useOrderDraft } from '../../features/ordering/orderDraftContext'
import { DRINK_FIELD_LABELS, DRINK_FIELDS, type DrinkSelection } from '../../features/ordering/types'
import { useOrdering } from '../../features/ordering/useOrdering'

const HEADING_ID = 'drinks-heading'

/** Messages for the choices a milkshake is still missing. */
function missingChoices(drink: DrinkSelection, index: number): Record<string, string> {
  const errors: Record<string, string> = {}
  for (const field of DRINK_FIELDS) {
    if (drink[field] === null) {
      errors[DRAFT_FIELDS.drinkChoice(index, field)] = `Choose a ${DRINK_FIELD_LABELS[field]} for milkshake ${index + 1}.`
    }
  }
  return errors
}

function incompleteIndexes(drinks: DrinkSelection[]): Set<number> {
  return new Set(drinks.flatMap((drink, index) => (isDrinkComplete(drink) ? [] : [index])))
}

/** /order/drinks — exactly one flavour, consistency and topping for every milkshake. */
export function OrderDrinksPage() {
  const { options } = useOrdering()
  const { draft, dispatch } = useOrderDraft()
  const navigate = useNavigate()
  const [editing, setEditing] = useState(() => incompleteIndexes(draft.drinks))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [removeIndex, setRemoveIndex] = useState<number | null>(null)
  const focusAfterRender = useRef<string | null>(null)

  // Buttons appear and disappear as cards change mode, so focus is moved after React has rendered.
  useEffect(() => {
    if (focusAfterRender.current) {
      document.getElementById(focusAfterRender.current)?.focus()
      focusAfterRender.current = null
    }
  })

  if (draft.drinks.length === 0) {
    return <EmptyDraft />
  }

  const canRemove = draft.drinks.length > options.drinkLimits.min

  function withoutDrinkErrors(index: number, current: Record<string, string>) {
    const prefix = `${DRAFT_FIELDS.drink(index)}.`
    return Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith(prefix)))
  }

  function handleDone(index: number) {
    const drink = draft.drinks[index]
    if (!drink) {
      return
    }
    const missing = missingChoices(drink, index)
    const first = Object.keys(missing).at(0)
    if (first) {
      setErrors((current) => ({ ...withoutDrinkErrors(index, current), ...missing }))
      document.getElementById(first)?.focus()
      return
    }
    setErrors((current) => withoutDrinkErrors(index, current))
    setEditing((current) => new Set([...current].filter((value) => value !== index)))
    focusAfterRender.current = `drink-${index}-edit`
  }

  function handleEdit(index: number) {
    setEditing((current) => new Set(current).add(index))
    focusAfterRender.current = DRAFT_FIELDS.drinkChoice(index, 'flavourId')
  }

  function handleRemove() {
    if (removeIndex === null) {
      return
    }
    const remaining = draft.drinks.filter((_, index) => index !== removeIndex)
    dispatch({ type: 'removeDrink', index: removeIndex })
    setRemoveIndex(null)
    setErrors({})
    setEditing(incompleteIndexes(remaining))
    focusAfterRender.current = HEADING_ID
  }

  function handleContinue() {
    const missing = Object.assign({}, ...draft.drinks.map(missingChoices)) as Record<string, string>
    const first = Object.keys(missing).at(0)
    if (first) {
      setErrors(missing)
      setEditing(incompleteIndexes(draft.drinks))
      focusAfterRender.current = first
      return
    }
    void navigate('/order/pickup')
  }

  const errorCount = Object.keys(errors).length

  return (
    <section aria-labelledby={HEADING_ID} className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 id={HEADING_ID} tabIndex={-1} className="text-2xl font-bold tracking-tight focus:outline-none">
        Choose your milkshakes
      </h1>
      <p className="mt-1 text-sm text-slate-600">{FEES_NOTE}</p>

      {errorCount > 0 && (
        <p role="alert" className="mt-4 rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
          {errorCount === 1 ? 'One choice is missing.' : `${errorCount} choices are missing.`} Please complete every milkshake.
        </p>
      )}

      <ol className="mt-6 space-y-4">
        {draft.drinks.map((drink, index) => (
          <DrinkCard
            // Position is the identity: milkshakes are numbered, and removing one renumbers the rest.
            key={index}
            index={index}
            drink={drink}
            options={options}
            editing={editing.has(index)}
            errors={errors}
            canRemove={canRemove}
            onChoose={(field, id) => {
              dispatch({ type: 'setDrinkChoice', index, field, id })
              const key = DRAFT_FIELDS.drinkChoice(index, field)
              setErrors((current) => Object.fromEntries(Object.entries(current).filter(([name]) => name !== key)))
            }}
            onDone={() => handleDone(index)}
            onEdit={() => handleEdit(index)}
            onRemove={() => setRemoveIndex(index)}
          />
        ))}
      </ol>

      <div className="mt-6 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={handleContinue}
          className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
        >
          Continue to pickup
        </button>
      </div>

      <ConfirmDialog
        open={removeIndex !== null}
        title={`Remove milkshake ${(removeIndex ?? 0) + 1}?`}
        message="This milkshake and its choices will be removed from your order. The milkshakes after it are renumbered."
        confirmLabel="Remove"
        cancelLabel="Keep it"
        onConfirm={handleRemove}
        onCancel={() => setRemoveIndex(null)}
      />
    </section>
  )
}