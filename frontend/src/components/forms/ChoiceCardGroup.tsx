import type { ReactNode } from 'react'
import { Icon } from '../ui/Icon'

export interface ChoiceCardOption {
  value: string
  label: string
  /** Secondary text on the card, e.g. a price ("+ R 10,00") or "Included". */
  detail?: string
  /** Optional decorative element shown before the label (e.g. a colour swatch); hidden from screen readers. */
  visual?: ReactNode
}

interface ChoiceCardGroupProps {
  /** Group name: becomes the fieldset legend and the radios' shared name prefix. */
  legend: string
  name: string
  options: readonly ChoiceCardOption[]
  value: string
  onChange: (value: string) => void
  hint?: string
  error?: string
  disabled?: boolean
  /** Hide the legend visually (it is still read out), when a heading nearby already says the same. */
  legendHidden?: boolean
  /** Layout: "list" is one card per row (long details, e.g. addresses); "grid" fits two on phones, three on wide screens. */
  layout?: 'list' | 'grid'
  /**
   * Id of the fieldset, which can take focus programmatically (tabIndex -1): forms move focus here when the group has
   * an error, and screen readers then read the group's name and its error.
   */
  id?: string
}

/**
 * Pick exactly one option, shown as cards. Underneath it is a native radio group inside a fieldset, so arrow keys,
 * Tab, form semantics and screen readers behave as for any radio group. Selection is shown by border, tint and a
 * check mark, never by colour alone.
 */
export function ChoiceCardGroup({
  legend,
  name,
  options,
  value,
  onChange,
  hint,
  error,
  disabled = false,
  legendHidden = false,
  layout = 'grid',
  id,
}: ChoiceCardGroupProps) {
  const hintId = hint ? `${name}-hint` : undefined
  const errorId = error ? `${name}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined
  const grid = layout === 'grid'

  return (
    <fieldset id={id} tabIndex={id ? -1 : undefined} aria-describedby={describedBy} disabled={disabled} className="min-w-0">
      <legend className={legendHidden ? 'sr-only' : 'text-base font-semibold text-chocolate'}>{legend}</legend>
      {hint && (
        <p id={hintId} className="mt-1 text-sm text-warm-grey">
          {hint}
        </p>
      )}
      <div className={`mt-3 grid gap-2.5 sm:gap-3 ${grid ? 'grid-cols-2 lg:grid-cols-3' : 'grid-cols-1'}`}>
        {options.map((option) => {
          const id = `${name}-${option.value}`
          const checked = option.value === value
          return (
            <label
              key={option.value}
              htmlFor={id}
              className={`relative flex cursor-pointer gap-3 rounded-control border-2 bg-surface transition-colors has-[:focus-visible]:outline-3 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-raspberry has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50 ${
                grid
                  ? 'min-h-20 flex-col items-start justify-center p-3 pr-10 sm:flex-row sm:items-center sm:justify-start sm:px-4 sm:pr-11'
                  : 'min-h-16 items-center px-4 py-3 pr-12'
              } ${checked ? 'border-raspberry bg-strawberry-wash' : 'border-line hover:border-line-strong'} ${
                error && !checked ? 'border-danger/40' : ''
              }`}
            >
              <input
                id={id}
                type="radio"
                name={name}
                value={option.value}
                checked={checked}
                onChange={() => onChange(option.value)}
                className="sr-only"
              />
              {option.visual && (
                <span aria-hidden="true" className="shrink-0">
                  {option.visual}
                </span>
              )}
              <span className="min-w-0">
                <span className="block leading-snug font-semibold text-chocolate">{option.label}</span>
                {option.detail && (
                  <span className="block text-sm text-warm-grey">
                    <span className="sr-only">, </span>
                    {option.detail}
                  </span>
                )}
              </span>
              <span
                aria-hidden="true"
                className={`absolute right-3 flex size-6 items-center justify-center rounded-full border-2 ${
                  grid ? 'top-3 sm:top-1/2 sm:-translate-y-1/2' : 'top-1/2 -translate-y-1/2'
                } ${checked ? 'border-raspberry bg-raspberry text-white' : 'border-line-strong bg-surface'}`}
              >
                {checked && <Icon name="check" className="size-3.5" />}
              </span>
            </label>
          )
        })}
      </div>
      {error && (
        <p id={errorId} className="mt-2 text-sm font-medium text-danger">
          {error}
        </p>
      )}
    </fieldset>
  )
}
