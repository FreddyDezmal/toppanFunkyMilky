import { SelectField } from '../../components/forms/SelectField'
import { formatMoney } from '../../lib/format/money'
import { isDrinkComplete } from './draft'
import { DRAFT_FIELDS } from './errors'
import { optionLabel } from './format'
import {
  DRINK_FIELD_LABELS,
  DRINK_FIELD_OPTIONS,
  DRINK_FIELDS,
  type DrinkField,
  type DrinkSelection,
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

/** One milkshake: three drop-downs while editing, a summary with Edit and Remove when done. */
export function DrinkCard({ index, drink, options, editing, errors, canRemove, price, onChoose, onDone, onEdit, onRemove }: DrinkCardProps) {
  const number = index + 1
  const locale = options.display.locale
  const headingId = `drink-${index}-heading`

  return (
    <li aria-labelledby={headingId} className="rounded-xl p-4 ring-1 ring-slate-200">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 id={headingId} className="font-semibold text-slate-900">
            Milkshake {number}
          </h2>
          <p className="text-sm text-slate-600">
            {price !== null ? (
              <>
                Price: <span className="font-medium text-slate-900">{formatMoney(price, options.display.currencyCode, locale)}</span>
              </>
            ) : isDrinkComplete(drink) ? (
              'Working out the price…'
            ) : (
              'Price shown once all three choices are made.'
            )}
          </p>
        </div>
        <div className="flex gap-2">
          {!editing && (
            <button
              id={`drink-${index}-edit`}
              type="button"
              onClick={onEdit}
              aria-label={`Edit milkshake ${number}`}
              className="rounded-md px-2 py-1 text-sm font-medium text-brand-700 hover:bg-brand-50"
            >
              Edit
            </button>
          )}
          {canRemove && (
            <button
              type="button"
              onClick={onRemove}
              aria-label={`Remove milkshake ${number}`}
              className="rounded-md px-2 py-1 text-sm font-medium text-rose-700 hover:bg-rose-50"
            >
              Remove
            </button>
          )}
        </div>
      </div>

      {editing ? (
        <div className="mt-3 grid gap-4 sm:grid-cols-3">
          {DRINK_FIELDS.map((field) => {
            const id = DRAFT_FIELDS.drinkChoice(index, field)
            const value = drink[field]
            return (
              <SelectField
                key={field}
                id={id}
                label={TITLES[field]}
                value={value === null ? '' : String(value)}
                placeholder={`Choose a ${DRINK_FIELD_LABELS[field]}`}
                options={options[DRINK_FIELD_OPTIONS[field]].map((option) => ({
                  value: String(option.id),
                  label: optionLabel(option, locale),
                }))}
                error={errors[id]}
                onChange={(selected) => onChoose(field, selected === '' ? null : Number(selected))}
              />
            )
          })}
          <div className="sm:col-span-3">
            <button
              type="button"
              onClick={onDone}
              aria-label={`Done with milkshake ${number}`}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
            >
              Done
            </button>
          </div>
        </div>
      ) : (
        <dl className="mt-2 grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
          {DRINK_FIELDS.map((field) => {
            const option = options[DRINK_FIELD_OPTIONS[field]].find((candidate) => candidate.id === drink[field])
            return (
              <div key={field} className="contents">
                <dt className="text-slate-600">{TITLES[field]}</dt>
                <dd className="text-slate-900">
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