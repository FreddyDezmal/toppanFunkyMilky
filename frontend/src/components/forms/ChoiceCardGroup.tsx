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
  /** Grid columns from the sm breakpoint (default 2; 3 for short labels). */
  columns?: 2 | 3
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
  columns = 2,
}: ChoiceCardGroupProps) {
  const hintId = hint ? `${name}-hint` : undefined
  const errorId = error ? `${name}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined

  return (
    <fieldset aria-describedby={describedBy} disabled={disabled}>
      <legend className={legendHidden ? 'sr-only' : 'text-base font-semibold text-chocolate'}>{legend}</legend>
      {hint && (
        <p id={hintId} className="mt-1 text-sm text-warm-grey">
          {hint}
        </p>
      )}
      <div className={`mt-3 grid grid-cols-1 gap-3 ${columns === 3 ? 'sm:grid-cols-3' : 'sm:grid-cols-2'}`}>
        {options.map((option) => {
          const id = `${name}-${option.value}`
          const checked = option.value === value
          return (
            <label
              key={option.value}
              htmlFor={id}
              className={`relative flex min-h-16 cursor-pointer items-center gap-3 rounded-control border-2 bg-surface px-4 py-3 transition-colors has-[:focus-visible]:outline-3 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-raspberry has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50 ${
                checked ? 'border-raspberry bg-strawberry-wash' : 'border-line hover:border-line-strong'
              } ${error && !checked ? 'border-danger/40' : ''}`}
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
              {option.visual && <span aria-hidden="true">{option.visual}</span>}
              <span className="min-w-0 flex-1">
                <span className="block font-semibold text-chocolate">{option.label}</span>
                {option.detail && (
                  <span className="block text-sm text-warm-grey">
                    <span className="sr-only">, </span>
                    {option.detail}
                  </span>
                )}
              </span>
              <span
                aria-hidden="true"
                className={`flex size-6 shrink-0 items-center justify-center rounded-full border-2 ${
                  checked ? 'border-raspberry bg-raspberry text-white' : 'border-line-strong bg-surface'
                }`}
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
