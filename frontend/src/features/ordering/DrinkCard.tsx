import { ChoiceCardGroup, type ChoiceCardOption } from '../../components/forms/ChoiceCardGroup'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'
import { formatMoney } from '../../lib/format/money'
import { isDrinkComplete } from './draft'
import { DRAFT_FIELDS } from './errors'
import { flavourSwatch } from './flavourSwatch'
import {
  DRINK_FIELD_OPTIONS,
  DRINK_FIELDS,
  type DrinkField,
  type DrinkSelection,
  type OrderingOption,
  type OrderingOptions,
} from './types'

const TITLES: Record<DrinkField, string> = { flavourId: 'Flavour', consistencyId: 'Consistency', toppingId: 'Topping' }

interface DrinkCardProps {
  index: number
  drink: DrinkSelection
  options: OrderingOptions
  editing: boolean
  /** Messages keyed by DRAFT_FIELDS.drinkChoice(index, field). */
  errors: Record<string, string>
  canRemove: boolean
  /** This milkshake's price from the latest server quote, or null when not (yet) priced. */
  price: number | null
  onChoose: (field: DrinkField, id: number | null) => void
  onDone: () => void
  onEdit: () => void
  onRemove: () => void
}

/**
 * The fee shown on a card. The flavour is the base of the milkshake; consistencies and toppings add to it, and a
 * free one is "Included". Current fees for information only: the server prices the milkshake.
 */
function feeText(field: DrinkField, option: OrderingOption, locale: string): string {
  const money = formatMoney(option.fee.amount, option.fee.currencyCode, locale)
  if (field === 'flavourId') {
    return money
  }
  return option.fee.amount === 0 ? 'Included' : `+ ${money}`
}

/** One milkshake: three groups of choice cards while editing, a summary with Edit and Remove when done. */
export function DrinkCard({ index, drink, options, editing, errors, canRemove, price, onChoose, onDone, onEdit, onRemove }: DrinkCardProps) {
  const number = index + 1
  const locale = options.display.locale
  const headingId = `drink-${index}-heading`
  const complete = isDrinkComplete(drink)

  return (
    <li aria-labelledby={headingId} className="rounded-card border border-line bg-surface p-4 shadow-soft sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <span
            aria-hidden="true"
            className={`flex size-10 shrink-0 items-center justify-center rounded-full font-display text-lg font-semibold ${
              complete && !editing ? 'bg-raspberry text-white' : 'bg-strawberry-tint text-raspberry-dark'
            }`}
          >
            {complete && !editing ? <Icon name="check" className="size-5" /> : number}
          </span>
          <div>
            <h2 id={headingId} className="font-display text-xl font-semibold text-chocolate">
              Milkshake {number}
            </h2>
            <p className="text-sm text-warm-grey">
              {price !== null ? (
                <>
                  Price: <span className="font-semibold text-chocolate">{formatMoney(price, options.display.currencyCode, locale)}</span>
                </>
              ) : complete ? (
                'Working out the price…'
              ) : (
                'Price shown once all three choices are made.'
              )}
            </p>
          </div>
        </div>
        <div className="flex gap-1">
          {!editing && (
            <Button id={`drink-${index}-edit`} variant="ghost" size="sm" onClick={onEdit} aria-label={`Edit milkshake ${number}`}>
              Edit
            </Button>
          )}
          {canRemove && (
            <Button
              variant="ghost"
              size="sm"
              onClick={onRemove}
              aria-label={`Remove milkshake ${number}`}
              className="text-danger hover:bg-danger-tint hover:text-danger"
            >
              Remove
            </Button>
          )}
        </div>
      </div>

      {editing ? (
        <div className="mt-6 space-y-7">
          {DRINK_FIELDS.map((field) => {
            const id = DRAFT_FIELDS.drinkChoice(index, field)
            const value = drink[field]
            const choices: ChoiceCardOption[] = options[DRINK_FIELD_OPTIONS[field]].map((option) => ({
              value: String(option.id),
              label: option.name,
              detail: feeText(field, option, locale),
              visual:
                field === 'flavourId' ? (
                  <span className={`block size-7 rounded-full ring-2 ring-surface ${flavourSwatch(option.name)}`} />
                ) : undefined,
            }))
            return (
              <ChoiceCardGroup
                key={field}
                id={id}
                name={`drink-${index}-${field}`}
                legend={TITLES[field]}
                options={choices}
                value={value === null ? '' : String(value)}
                error={errors[id]}
                onChange={(selected) => onChoose(field, Number(selected))}
              />
            )
          })}
          <Button variant="secondary" onClick={onDone} aria-label={`Done with milkshake ${number}`} icon={<Icon name="check" className="size-4" />}>
            Done
          </Button>
        </div>
      ) : (
        <dl className="mt-4 grid gap-2 text-sm sm:grid-cols-3">
          {DRINK_FIELDS.map((field) => {
            const option = options[DRINK_FIELD_OPTIONS[field]].find((candidate) => candidate.id === drink[field])
            return (
              <div key={field} className="rounded-control bg-cream px-3 py-2">
                <dt className="text-warm-grey">{TITLES[field]}</dt>
                <dd className="font-semibold text-chocolate">
                  {option ? `${option.name} · ${formatMoney(option.fee.amount, option.fee.currencyCode, locale)}` : 'Not chosen'}
                </dd>
              </div>
            )
          })}
        </dl>
      )}
    </li>
  )
}
