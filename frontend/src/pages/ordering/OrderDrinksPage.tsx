import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { Alert } from '../../components/ui/Alert'
import { Button } from '../../components/ui/Button'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { Icon } from '../../components/ui/Icon'
import { isDrinkComplete } from '../../features/ordering/draft'
import { DrinkCard } from '../../features/ordering/DrinkCard'
import { EmptyDraft } from '../../features/ordering/EmptyDraft'
import { DRAFT_FIELDS } from '../../features/ordering/errors'
import { ESTIMATE_NOTE } from '../../features/ordering/format'
import { useOrderDraft } from '../../features/ordering/orderDraftContext'
import { PriceSummary, PriceSummaryPlaceholder } from '../../features/ordering/PriceSummary'
import { DRINK_FIELD_LABELS, DRINK_FIELDS, type DrinkSelection } from '../../features/ordering/types'
import { useLiveQuote, type LiveQuote } from '../../features/ordering/useLiveQuote'
import { useOrdering } from '../../features/ordering/useOrdering'
import { formatMoney } from '../../lib/format/money'

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

/** One short sentence for screen readers, announced politely as prices change. */
function estimateStatus(live: LiveQuote, locale: string): string {
  switch (live.status) {
    case 'idle':
      return ''
    case 'updating':
      return 'Updating prices…'
    case 'error':
      return live.message
    case 'ready':
      return live.quote.totals
        ? `Estimated total ${formatMoney(live.quote.totals.total, live.quote.totals.currencyCode, locale)}.`
        : 'Complete every milkshake to see the total.'
  }
}

function incompleteIndexes(drinks: DrinkSelection[]): Set<number> {
  return new Set(drinks.flatMap((drink, index) => (isDrinkComplete(drink) ? [] : [index])))
}

/** /order/drinks — exactly one flavour, consistency and topping for every milkshake. */
export function OrderDrinksPage() {
  const { options, reloadOptions } = useOrdering()
  const { draft, dispatch } = useOrderDraft()
  const navigate = useNavigate()
  const live = useLiveQuote(draft.drinks, reloadOptions)
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
  const currency = options.display.currencyCode
  const locale = options.display.locale
  const totals = live.quote?.totals ?? null

  return (
    // Phones: room at the bottom for the fixed action bar.
    <div className="grid gap-8 pb-28 lg:grid-cols-[minmax(0,1fr)_22rem] lg:pb-0">
      <section aria-labelledby={HEADING_ID} className="min-w-0">
        <h1 id={HEADING_ID} tabIndex={-1} className="text-3xl font-semibold text-chocolate focus:outline-none sm:text-4xl">
          Choose your milkshakes
        </h1>
        <p className="mt-2 text-warm-grey">{ESTIMATE_NOTE}</p>

        {errorCount > 0 && (
          <Alert tone="danger" role="alert" className="mt-4">
            {errorCount === 1 ? 'One choice is missing.' : `${errorCount} choices are missing.`} Please complete every milkshake.
          </Alert>
        )}

        <ol className="mt-6 space-y-5">
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
              price={live.quote?.drinks.find((line) => line.number === index + 1)?.price ?? null}
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
      </section>

      {/* Summary: beside the builder and kept in view on wide screens; after it on phones. */}
      <aside aria-label="Order summary" className="lg:sticky lg:top-24 lg:self-start">
        <div className="rounded-card border border-line bg-surface p-5 shadow-soft sm:p-6">
          <h2 className="font-display text-xl font-semibold text-chocolate">Your order</h2>
          <ul className="mt-3 divide-y divide-line text-sm">
            {draft.drinks.map((drink, index) => {
              const price = live.quote?.drinks.find((line) => line.number === index + 1)?.price ?? null
              return (
                <li key={index} className="flex min-h-9 items-center justify-between gap-3 py-1.5">
                  <span className="text-chocolate">Milkshake {index + 1}</span>
                  <span className={price !== null ? 'font-semibold text-chocolate tabular-nums' : 'text-warm-grey'}>
                    {price !== null ? formatMoney(price, currency, locale) : isDrinkComplete(drink) ? '…' : 'Not finished'}
                  </span>
                </li>
              )
            })}
          </ul>

          <section aria-labelledby="estimate-heading" aria-busy={live.status === 'updating'} className="mt-4 rounded-control bg-cream p-4">
            <h2 id="estimate-heading" className="font-semibold text-chocolate">
              Estimated price
            </h2>
            {/* One reserved line, so a longer or shorter message never moves what is below it. */}
            <p role="status" className="mt-1 min-h-10 text-sm text-warm-grey">
              {estimateStatus(live, locale)}
            </p>
            {/* The table's space is kept while there is no estimate, so "Continue to pickup" never moves (DEF-04). */}
            <div className="mt-2">
              {totals ? <PriceSummary totals={totals} locale={locale} /> : <PriceSummaryPlaceholder />}
            </div>
          </section>
        </div>

        {/* One Continue button: a bar fixed to the bottom of the screen on phones, part of the summary on wide screens. */}
        <div className="fixed inset-x-0 bottom-0 z-30 border-t border-line bg-surface/95 px-4 py-3 shadow-raised backdrop-blur lg:static lg:mt-4 lg:border-0 lg:bg-transparent lg:p-0 lg:shadow-none lg:backdrop-blur-none">
          <div className="mx-auto flex max-w-xl items-center gap-4 lg:max-w-none">
            <p aria-hidden="true" className="min-w-0 flex-1 text-sm text-warm-grey lg:hidden">
              {draft.drinks.length === 1 ? '1 milkshake' : `${draft.drinks.length} milkshakes`}
              <span className="block font-display text-lg font-semibold text-chocolate tabular-nums">
                {totals ? formatMoney(totals.total, totals.currencyCode, locale) : '—'}
              </span>
            </p>
            <Button size="lg" onClick={handleContinue} className="shrink-0 lg:w-full">
              Continue to pickup
              <Icon name="arrow-right" />
            </Button>
          </div>
        </div>
      </aside>

      <ConfirmDialog
        open={removeIndex !== null}
        title={`Remove milkshake ${(removeIndex ?? 0) + 1}?`}
        message="This milkshake and its choices will be removed from your order. The milkshakes after it are renumbered."
        confirmLabel="Remove"
        cancelLabel="Keep it"
        onConfirm={handleRemove}
        onCancel={() => setRemoveIndex(null)}
      />
    </div>
  )
}
